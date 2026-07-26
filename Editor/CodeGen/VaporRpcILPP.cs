using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;
using ILPPInterface = Unity.CompilationPipeline.Common.ILPostProcessing.ILPostProcessor;

namespace Vapor.CodeGen
{
    /// <summary>
    /// Weaves every [VaporRpc] method on VaporNetworkObject subclasses, in every assembly that
    /// references vapor.core.runtime:
    /// <list type="bullet">
    /// <item>The method body gets a prologue: when called normally it serializes its arguments, sends to
    /// the targets resolved from the attribute's SendTo, and only falls through into the original body
    /// when the local peer is itself a target. When invoked by the receive dispatch (signalled through
    /// VaporNetworkObject.__rpc_exec) it clears the flag and runs the body directly.</item>
    /// <item>A static __rpc_handler_&lt;hash&gt; method is generated on the declaring type that
    /// deserializes the arguments and invokes the method.</item>
    /// <item>A __GEN.VaporRpcInitializer class is generated per assembly that registers every handler
    /// with VaporNetworkObject.__registerRpc at subsystem registration.</item>
    /// </list>
    /// The rpc id is xxHash32 of "{module name} / {method full name}", baked into the send prologue,
    /// the handler name, and the registration call at weave time.
    /// </summary>
    public sealed class VaporRpcILPP : ILPPInterface
    {
        private const string k_RuntimeAssemblyName = "vapor.core.runtime";
        private const string k_VaporNetworkObjectFullName = "Vapor.NetworkObjects.VaporNetworkObject";
        private const string k_VaporRpcAttributeFullName = "Vapor.NetworkObjects.VaporRpcAttribute";
        private const string k_INetworkPacketFullName = "Vapor.NetworkObjects.INetworkPacket";
        private const string k_NetworkBehaviourFullName = "Unity.Netcode.NetworkBehaviour";
        private const string k_INetworkSerializableFullName = "Unity.Netcode.INetworkSerializable";
        private const string k_FastBufferReaderFullName = "Unity.Netcode.FastBufferReader";
        private const string k_UnityCoreModuleFileName = "UnityEngine.CoreModule.dll";
        private const string k_RuntimeInitializeAttributeFullName = "UnityEngine.RuntimeInitializeOnLoadMethodAttribute";
        private const string k_RuntimeInitializeLoadTypeFullName = "UnityEngine.RuntimeInitializeLoadType";
        private const int k_SubsystemRegistrationLoadType = 4; // UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration
        private const int k_DefaultNetworkDelivery = 3; // Unity.Netcode.NetworkDelivery.ReliableSequenced

        private readonly List<DiagnosticMessage> m_Diagnostics = new();

        private ModuleDefinition m_MainModule;
        private TypeReference m_VaporNetworkObject_TypeRef;
        private TypeReference m_RpcHandlerDelegate_TypeRef;
        private TypeReference m_FastBufferWriter_TypeRef;
        private TypeReference m_FastBufferReader_TypeRef;
        private TypeReference m_RuntimeInitializeLoadType_TypeRef;
        private FieldReference m_RpcExec_FieldRef;
        private MethodReference m_BeginSendRpc_MethodRef;
        private MethodReference m_EndSendRpc_MethodRef;
        private MethodReference m_RegisterRpc_MethodRef;
        private MethodReference m_WriteValue_MethodRef;
        private MethodReference m_ReadValue_MethodRef;
        private MethodReference m_WriteNetworkObject_MethodRef;
        private MethodReference m_ReadNetworkObject_MethodRef;
        private MethodReference m_WriteNetworkBehaviour_MethodRef;
        private MethodReference m_ReadNetworkBehaviour_MethodRef;
        private MethodReference m_WritePacket_MethodRef;
        private MethodReference m_ReadPacket_MethodRef;
        private MethodReference m_WriteSerializable_MethodRef;
        private MethodReference m_ReadSerializable_MethodRef;
        private MethodReference m_RpcHandlerDelegateCtor_MethodRef;
        private MethodReference m_RuntimeInitializeOnLoadAttribute_CtorRef;

        public override ILPPInterface GetInstance() => this;

        public override bool WillProcess(ICompiledAssembly compiledAssembly) =>
            compiledAssembly.Name == k_RuntimeAssemblyName ||
            compiledAssembly.References.Any(filePath => Path.GetFileNameWithoutExtension(filePath) == k_RuntimeAssemblyName);

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            if (!WillProcess(compiledAssembly))
            {
                return new ILPostProcessResult(compiledAssembly.InMemoryAssembly);
            }

            m_Diagnostics.Clear();

            var assemblyDefinition = CodeGenHelpers.AssemblyDefinitionFor(compiledAssembly, out var assemblyResolver);
            if (assemblyDefinition == null)
            {
                m_Diagnostics.AddError($"Cannot read assembly definition: {compiledAssembly.Name}");
                return new ILPostProcessResult(compiledAssembly.InMemoryAssembly, m_Diagnostics);
            }

            var mainModule = assemblyDefinition.MainModule;

            // Collect every [VaporRpc] method before importing anything, so assemblies without rpcs
            // pay only for this scan.
            var rpcTypes = new List<(TypeDefinition Type, List<(MethodDefinition Method, CustomAttribute Attribute)> Rpcs)>();
            foreach (var typeDefinition in mainModule.GetTypes())
            {
                List<(MethodDefinition, CustomAttribute)> rpcs = null;
                foreach (var methodDefinition in typeDefinition.Methods)
                {
                    var attribute = methodDefinition.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == k_VaporRpcAttributeFullName);
                    if (attribute == null)
                    {
                        continue;
                    }

                    if (!typeDefinition.IsSubclassOf(k_VaporNetworkObjectFullName) && typeDefinition.FullName != k_VaporNetworkObjectFullName)
                    {
                        m_Diagnostics.AddError(methodDefinition, $"[VaporRpc] method {typeDefinition.FullName}::{methodDefinition.Name} must be declared on a subclass of VaporNetworkObject.");
                        continue;
                    }

                    rpcs ??= new List<(MethodDefinition, CustomAttribute)>();
                    rpcs.Add((methodDefinition, attribute));
                }

                if (rpcs != null)
                {
                    rpcTypes.Add((typeDefinition, rpcs));
                }
            }

            if (rpcTypes.Count == 0)
            {
                return m_Diagnostics.Count > 0 ? new ILPostProcessResult(compiledAssembly.InMemoryAssembly, m_Diagnostics) : null;
            }

            if (!ImportReferences(assemblyDefinition, mainModule, assemblyResolver))
            {
                return new ILPostProcessResult(compiledAssembly.InMemoryAssembly, m_Diagnostics);
            }

            var registrations = new List<(uint Hash, MethodDefinition Handler, string Name)>();
            try
            {
                foreach (var (typeDefinition, rpcs) in rpcTypes)
                {
                    foreach (var (methodDefinition, attribute) in rpcs)
                    {
                        ProcessRpcMethod(typeDefinition, methodDefinition, attribute, registrations);
                    }
                }

                if (registrations.Count > 0)
                {
                    EmitRegistrationInitializer(mainModule, registrations);
                }

                mainModule.RemoveRecursiveReferences();
            }
            catch (Exception e)
            {
                m_Diagnostics.AddError((e.ToString() + e.StackTrace).Replace("\n", "|").Replace("\r", "|"));
                return new ILPostProcessResult(compiledAssembly.InMemoryAssembly, m_Diagnostics);
            }

            var pe = new MemoryStream();
            var pdb = new MemoryStream();
            var writerParameters = new WriterParameters
            {
                SymbolWriterProvider = new PortablePdbWriterProvider(),
                SymbolStream = pdb,
                WriteSymbols = true
            };

            assemblyDefinition.Write(pe, writerParameters);
            return new ILPostProcessResult(new InMemoryAssembly(pe.ToArray(), pdb.ToArray()), m_Diagnostics);
        }

        #region - Reference Import -

        private bool ImportReferences(AssemblyDefinition assemblyDefinition, ModuleDefinition mainModule, PostProcessorAssemblyResolver assemblyResolver)
        {
            m_MainModule = mainModule;

            var vaporModule = CodeGenHelpers.FindModule(assemblyDefinition, assemblyResolver, $"{k_RuntimeAssemblyName}.dll");
            if (vaporModule == null)
            {
                m_Diagnostics.AddError($"Cannot resolve module {k_RuntimeAssemblyName}.dll while processing {mainModule.Name}.");
                return false;
            }

            var unityModule = CodeGenHelpers.FindModule(assemblyDefinition, assemblyResolver, k_UnityCoreModuleFileName);
            if (unityModule == null)
            {
                m_Diagnostics.AddError($"Cannot resolve module {k_UnityCoreModuleFileName} while processing {mainModule.Name}.");
                return false;
            }

            var vaporNetworkObjectDef = vaporModule.GetTypes().FirstOrDefault(t => t.FullName == k_VaporNetworkObjectFullName);
            if (vaporNetworkObjectDef == null)
            {
                m_Diagnostics.AddError($"Cannot find {k_VaporNetworkObjectFullName} in {vaporModule.Name}.");
                return false;
            }

            m_VaporNetworkObject_TypeRef = mainModule.ImportReference(vaporNetworkObjectDef);

            m_RpcExec_FieldRef = null;
            foreach (var fieldDefinition in vaporNetworkObjectDef.Fields)
            {
                if (fieldDefinition.Name == "__rpc_exec")
                {
                    m_RpcExec_FieldRef = mainModule.ImportReference(fieldDefinition);
                }
            }

            MethodDefinition beginSendRpcDef = null;
            m_BeginSendRpc_MethodRef = null;
            m_EndSendRpc_MethodRef = null;
            m_RegisterRpc_MethodRef = null;
            m_WriteValue_MethodRef = null;
            m_ReadValue_MethodRef = null;
            m_WriteNetworkObject_MethodRef = null;
            m_ReadNetworkObject_MethodRef = null;
            m_WriteNetworkBehaviour_MethodRef = null;
            m_ReadNetworkBehaviour_MethodRef = null;
            m_WritePacket_MethodRef = null;
            m_ReadPacket_MethodRef = null;
            m_WriteSerializable_MethodRef = null;
            m_ReadSerializable_MethodRef = null;
            foreach (var methodDefinition in vaporNetworkObjectDef.Methods)
            {
                switch (methodDefinition.Name)
                {
                    case "__beginSendRpc":
                        beginSendRpcDef = methodDefinition;
                        m_BeginSendRpc_MethodRef = mainModule.ImportReference(methodDefinition);
                        break;
                    case "__endSendRpc":
                        m_EndSendRpc_MethodRef = mainModule.ImportReference(methodDefinition);
                        break;
                    case "__registerRpc":
                        m_RegisterRpc_MethodRef = mainModule.ImportReference(methodDefinition);
                        break;
                    case "__writeValue":
                        m_WriteValue_MethodRef = mainModule.ImportReference(methodDefinition);
                        break;
                    case "__readValue":
                        m_ReadValue_MethodRef = mainModule.ImportReference(methodDefinition);
                        break;
                    case "__writeNetworkObject":
                        m_WriteNetworkObject_MethodRef = mainModule.ImportReference(methodDefinition);
                        break;
                    case "__readNetworkObject":
                        m_ReadNetworkObject_MethodRef = mainModule.ImportReference(methodDefinition);
                        break;
                    case "__writeNetworkBehaviour":
                        m_WriteNetworkBehaviour_MethodRef = mainModule.ImportReference(methodDefinition);
                        break;
                    case "__readNetworkBehaviour":
                        m_ReadNetworkBehaviour_MethodRef = mainModule.ImportReference(methodDefinition);
                        break;
                    case "__writePacket":
                        m_WritePacket_MethodRef = mainModule.ImportReference(methodDefinition);
                        break;
                    case "__readPacket":
                        m_ReadPacket_MethodRef = mainModule.ImportReference(methodDefinition);
                        break;
                    case "__writeSerializable":
                        m_WriteSerializable_MethodRef = mainModule.ImportReference(methodDefinition);
                        break;
                    case "__readSerializable":
                        m_ReadSerializable_MethodRef = mainModule.ImportReference(methodDefinition);
                        break;
                }
            }

            var handlerDelegateDef = vaporNetworkObjectDef.NestedTypes.FirstOrDefault(t => t.Name == "__RpcReceiveHandler");
            if (handlerDelegateDef == null)
            {
                m_Diagnostics.AddError($"Cannot find {k_VaporNetworkObjectFullName}.__RpcReceiveHandler in {vaporModule.Name}.");
                return false;
            }

            m_RpcHandlerDelegate_TypeRef = mainModule.ImportReference(handlerDelegateDef);
            var handlerDelegateCtorDef = handlerDelegateDef.Methods.FirstOrDefault(m =>
                m.IsConstructor && m.Parameters.Count == 2 &&
                m.Parameters[0].ParameterType.FullName == "System.Object" &&
                m.Parameters[1].ParameterType.FullName == "System.IntPtr");
            if (handlerDelegateCtorDef == null)
            {
                m_Diagnostics.AddError("Cannot find the (object, IntPtr) constructor on __RpcReceiveHandler.");
                return false;
            }

            m_RpcHandlerDelegateCtor_MethodRef = mainModule.ImportReference(handlerDelegateCtorDef);

            if (beginSendRpcDef == null || m_EndSendRpc_MethodRef == null || m_RegisterRpc_MethodRef == null || m_RpcExec_FieldRef == null ||
                m_WriteValue_MethodRef == null || m_ReadValue_MethodRef == null ||
                m_WriteNetworkObject_MethodRef == null || m_ReadNetworkObject_MethodRef == null ||
                m_WriteNetworkBehaviour_MethodRef == null || m_ReadNetworkBehaviour_MethodRef == null ||
                m_WritePacket_MethodRef == null || m_ReadPacket_MethodRef == null ||
                m_WriteSerializable_MethodRef == null || m_ReadSerializable_MethodRef == null)
            {
                m_Diagnostics.AddError($"The rpc support surface on {k_VaporNetworkObjectFullName} is incomplete; the weaver and runtime are out of sync.");
                return false;
            }

            // FastBufferWriter from __beginSendRpc's out parameter, FastBufferReader from the handler
            // delegate's Invoke — avoids resolving the netcode module separately.
            var writerByRef = (ByReferenceType)beginSendRpcDef.Parameters[1].ParameterType;
            m_FastBufferWriter_TypeRef = mainModule.ImportReference(writerByRef.ElementType);

            var invokeDef = handlerDelegateDef.Methods.First(m => m.Name == "Invoke");
            m_FastBufferReader_TypeRef = mainModule.ImportReference(invokeDef.Parameters[1].ParameterType);

            var runtimeInitializeAttributeDef = unityModule.GetTypes().FirstOrDefault(t => t.FullName == k_RuntimeInitializeAttributeFullName);
            var runtimeInitializeLoadTypeDef = unityModule.GetTypes().FirstOrDefault(t => t.FullName == k_RuntimeInitializeLoadTypeFullName);
            if (runtimeInitializeAttributeDef == null || runtimeInitializeLoadTypeDef == null)
            {
                m_Diagnostics.AddError($"Cannot find {k_RuntimeInitializeAttributeFullName} in {unityModule.Name}.");
                return false;
            }

            m_RuntimeInitializeLoadType_TypeRef = mainModule.ImportReference(runtimeInitializeLoadTypeDef);
            var attributeCtorDef = runtimeInitializeAttributeDef.Methods.FirstOrDefault(m =>
                m.IsConstructor && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == k_RuntimeInitializeLoadTypeFullName);
            if (attributeCtorDef == null)
            {
                m_Diagnostics.AddError($"Cannot find the (RuntimeInitializeLoadType) constructor on {k_RuntimeInitializeAttributeFullName}.");
                return false;
            }

            m_RuntimeInitializeOnLoadAttribute_CtorRef = mainModule.ImportReference(attributeCtorDef);
            return true;
        }

        #endregion

        #region - Method Processing -

        private enum ReadCast
        {
            None,
            CastClass,
            UnboxAny
        }

        private readonly struct ParameterSerializer
        {
            public readonly MethodReference WriteMethod;
            public readonly MethodReference ReadMethod;
            public readonly bool BoxOnWrite;
            public readonly ReadCast Cast;

            public ParameterSerializer(MethodReference writeMethod, MethodReference readMethod, bool boxOnWrite, ReadCast cast)
            {
                WriteMethod = writeMethod;
                ReadMethod = readMethod;
                BoxOnWrite = boxOnWrite;
                Cast = cast;
            }
        }

        private void ProcessRpcMethod(TypeDefinition typeDefinition, MethodDefinition methodDefinition, CustomAttribute attribute,
            List<(uint Hash, MethodDefinition Handler, string Name)> registrations)
        {
            if (!ValidateRpcMethod(typeDefinition, methodDefinition))
            {
                return;
            }

            var serializers = new List<ParameterSerializer>();
            foreach (var parameter in methodDefinition.Parameters)
            {
                if (!TryGetParameterSerializer(parameter.ParameterType, methodDefinition, out var serializer))
                {
                    return;
                }

                serializers.Add(serializer);
            }

            var hash = methodDefinition.Hash();
            if (hash == 0)
            {
                m_Diagnostics.AddError(methodDefinition, $"The rpc id for {methodDefinition.FullName} hashed to 0; rename the method.");
                return;
            }

            int sendTo = (int)attribute.ConstructorArguments[0].Value;
            int networkDelivery = attribute.ConstructorArguments.Count > 1 ? (int)attribute.ConstructorArguments[1].Value : k_DefaultNetworkDelivery;

            var handler = GenerateReceiveHandler(typeDefinition, methodDefinition, serializers, hash);
            InjectSendPrologue(methodDefinition, serializers, hash, sendTo, networkDelivery);
            registrations.Add((hash, handler, $"{typeDefinition.FullName}::{methodDefinition.Name}"));
        }

        private bool ValidateRpcMethod(TypeDefinition typeDefinition, MethodDefinition methodDefinition)
        {
            bool valid = true;
            if (methodDefinition.IsStatic)
            {
                m_Diagnostics.AddError(methodDefinition, $"{methodDefinition.FullName}: [VaporRpc] methods cannot be static.");
                valid = false;
            }

            if (methodDefinition.IsAbstract || methodDefinition.IsVirtual)
            {
                m_Diagnostics.AddError(methodDefinition, $"{methodDefinition.FullName}: [VaporRpc] methods cannot be abstract, virtual, or overrides.");
                valid = false;
            }

            if (methodDefinition.HasGenericParameters || typeDefinition.HasGenericParameters)
            {
                m_Diagnostics.AddError(methodDefinition, $"{methodDefinition.FullName}: [VaporRpc] methods cannot be generic or declared on generic types.");
                valid = false;
            }

            if (methodDefinition.ReturnType.FullName != "System.Void")
            {
                m_Diagnostics.AddError(methodDefinition, $"{methodDefinition.FullName}: [VaporRpc] methods must return void.");
                valid = false;
            }

            if (!methodDefinition.Name.EndsWith("Rpc"))
            {
                m_Diagnostics.AddError(methodDefinition, $"{methodDefinition.FullName}: [VaporRpc] method names must end with 'Rpc'.");
                valid = false;
            }

            if (methodDefinition.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.AsyncStateMachineAttribute"))
            {
                m_Diagnostics.AddError(methodDefinition, $"{methodDefinition.FullName}: [VaporRpc] methods cannot be async.");
                valid = false;
            }

            foreach (var parameter in methodDefinition.Parameters)
            {
                if (parameter.ParameterType.FullName == k_FastBufferReaderFullName)
                {
                    m_Diagnostics.AddError(methodDefinition, $"{methodDefinition.FullName}: takes a FastBufferReader — the legacy manually-registered rpc pattern. Declare typed parameters instead; serialization is generated.");
                    valid = false;
                    continue;
                }

                if (parameter.ParameterType.IsByReference || parameter.IsOut || parameter.IsIn)
                {
                    m_Diagnostics.AddError(methodDefinition, $"{methodDefinition.FullName}: [VaporRpc] parameters cannot be ref, out, or in.");
                    valid = false;
                }

                if (parameter.ParameterType.IsPointer || parameter.ParameterType.ContainsGenericParameter)
                {
                    m_Diagnostics.AddError(methodDefinition, $"{methodDefinition.FullName}: parameter {parameter.Name} has an unsupported type {parameter.ParameterType.FullName}.");
                    valid = false;
                }
            }

            return valid;
        }

        private bool TryGetParameterSerializer(TypeReference parameterType, MethodDefinition methodDefinition, out ParameterSerializer serializer)
        {
            if (parameterType.IsSubclassOfOrEqual(k_VaporNetworkObjectFullName))
            {
                serializer = new ParameterSerializer(m_WriteNetworkObject_MethodRef, m_ReadNetworkObject_MethodRef, false,
                    parameterType.FullName == k_VaporNetworkObjectFullName ? ReadCast.None : ReadCast.CastClass);
                return true;
            }

            if (parameterType.IsSubclassOfOrEqual(k_NetworkBehaviourFullName))
            {
                serializer = new ParameterSerializer(m_WriteNetworkBehaviour_MethodRef, m_ReadNetworkBehaviour_MethodRef, false,
                    parameterType.FullName == k_NetworkBehaviourFullName ? ReadCast.None : ReadCast.CastClass);
                return true;
            }

            if (parameterType.ImplementsOrIsInterface(k_INetworkPacketFullName))
            {
                var cast = parameterType.IsValueType
                    ? ReadCast.UnboxAny
                    : parameterType.FullName == k_INetworkPacketFullName
                        ? ReadCast.None
                        : ReadCast.CastClass;
                serializer = new ParameterSerializer(m_WritePacket_MethodRef, m_ReadPacket_MethodRef, parameterType.IsValueType, cast);
                return true;
            }

            if (parameterType.ImplementsOrIsInterface(k_INetworkSerializableFullName))
            {
                if (!parameterType.HasParameterlessConstructor())
                {
                    m_Diagnostics.AddError(methodDefinition, $"{methodDefinition.FullName}: INetworkSerializable parameter type {parameterType.FullName} needs a parameterless constructor to be received.");
                    serializer = default;
                    return false;
                }

                serializer = new ParameterSerializer(MakeClosedGeneric(m_WriteSerializable_MethodRef, parameterType),
                    MakeClosedGeneric(m_ReadSerializable_MethodRef, parameterType), false, ReadCast.None);
                return true;
            }

            // Everything else — primitives, strings, vectors, enums, plain managed types — goes through
            // NetworkVariableSerialization<T>, same as the shortcut/fallback paths did at runtime before.
            serializer = new ParameterSerializer(MakeClosedGeneric(m_WriteValue_MethodRef, parameterType),
                MakeClosedGeneric(m_ReadValue_MethodRef, parameterType), false, ReadCast.None);
            return true;
        }

        private MethodReference MakeClosedGeneric(MethodReference openMethod, TypeReference argument)
        {
            var generic = new GenericInstanceMethod(openMethod);
            generic.GenericArguments.Add(m_MainModule.ImportReference(argument));
            return m_MainModule.ImportReference(generic);
        }

        #endregion

        #region - IL Emission -

        private void InjectSendPrologue(MethodDefinition methodDefinition, List<ParameterSerializer> serializers, uint hash, int sendTo, int networkDelivery)
        {
            var processor = methodDefinition.Body.GetILProcessor();
            methodDefinition.Body.InitLocals = true;

            var writerLocal = new VariableDefinition(m_FastBufferWriter_TypeRef);
            methodDefinition.Body.Variables.Add(writerLocal);

            var sendLabel = processor.Create(OpCodes.Nop);
            var writeLabel = processor.Create(OpCodes.Nop);
            var bodyLabel = processor.Create(OpCodes.Nop);

            var instructions = new List<Instruction>
            {
                // if (__rpc_exec) { __rpc_exec = false; goto body; }
                // Clearing the flag before the body runs lets the body send further rpcs normally.
                processor.Create(OpCodes.Ldarg_0),
                processor.Create(OpCodes.Ldfld, m_RpcExec_FieldRef),
                processor.Create(OpCodes.Brfalse, sendLabel),
                processor.Create(OpCodes.Ldarg_0),
                processor.Create(OpCodes.Ldc_I4_0),
                processor.Create(OpCodes.Stfld, m_RpcExec_FieldRef),
                processor.Create(OpCodes.Br, bodyLabel),

                // if (!__beginSendRpc(hash, out writer)) return;
                sendLabel,
                processor.Create(OpCodes.Ldarg_0),
                processor.Create(OpCodes.Ldc_I4, unchecked((int)hash)),
                processor.Create(OpCodes.Ldloca, writerLocal),
                processor.Create(OpCodes.Call, m_BeginSendRpc_MethodRef),
                processor.Create(OpCodes.Brtrue, writeLabel),
                processor.Create(OpCodes.Ret),
                writeLabel
            };

            for (int i = 0; i < methodDefinition.Parameters.Count; i++)
            {
                var parameter = methodDefinition.Parameters[i];
                var serializer = serializers[i];
                instructions.Add(processor.Create(OpCodes.Ldloc, writerLocal));
                instructions.Add(processor.Create(OpCodes.Ldarg, parameter));
                if (serializer.BoxOnWrite)
                {
                    instructions.Add(processor.Create(OpCodes.Box, m_MainModule.ImportReference(parameter.ParameterType)));
                }

                instructions.Add(processor.Create(OpCodes.Call, serializer.WriteMethod));
            }

            // if (!__endSendRpc(writer, sendTo, networkDelivery)) return;  — true means the local peer
            // is a target and the body must also run here.
            instructions.Add(processor.Create(OpCodes.Ldarg_0));
            instructions.Add(processor.Create(OpCodes.Ldloc, writerLocal));
            instructions.Add(processor.Create(OpCodes.Ldc_I4, sendTo));
            instructions.Add(processor.Create(OpCodes.Ldc_I4, networkDelivery));
            instructions.Add(processor.Create(OpCodes.Call, m_EndSendRpc_MethodRef));
            instructions.Add(processor.Create(OpCodes.Brtrue, bodyLabel));
            instructions.Add(processor.Create(OpCodes.Ret));
            instructions.Add(bodyLabel);

            instructions.Reverse();
            foreach (var instruction in instructions)
            {
                processor.Body.Instructions.Insert(0, instruction);
            }
        }

        private MethodDefinition GenerateReceiveHandler(TypeDefinition typeDefinition, MethodDefinition methodDefinition, List<ParameterSerializer> serializers, uint hash)
        {
            var handler = new MethodDefinition($"__rpc_handler_{hash}",
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                m_MainModule.TypeSystem.Void);
            handler.Parameters.Add(new ParameterDefinition("target", ParameterAttributes.None, m_VaporNetworkObject_TypeRef));
            handler.Parameters.Add(new ParameterDefinition("reader", ParameterAttributes.None, m_FastBufferReader_TypeRef));
            handler.Body.InitLocals = true;

            var processor = handler.Body.GetILProcessor();

            var locals = new List<VariableDefinition>();
            for (int i = 0; i < methodDefinition.Parameters.Count; i++)
            {
                var parameterType = methodDefinition.Parameters[i].ParameterType;
                var serializer = serializers[i];
                var local = new VariableDefinition(parameterType);
                handler.Body.Variables.Add(local);
                locals.Add(local);

                processor.Emit(OpCodes.Ldarg_1);
                processor.Emit(OpCodes.Call, serializer.ReadMethod);
                switch (serializer.Cast)
                {
                    case ReadCast.CastClass:
                        processor.Emit(OpCodes.Castclass, m_MainModule.ImportReference(parameterType));
                        break;
                    case ReadCast.UnboxAny:
                        processor.Emit(OpCodes.Unbox_Any, m_MainModule.ImportReference(parameterType));
                        break;
                }

                processor.Emit(OpCodes.Stloc, local);
            }

            processor.Emit(OpCodes.Ldarg_0);
            if (typeDefinition.FullName != k_VaporNetworkObjectFullName)
            {
                processor.Emit(OpCodes.Castclass, typeDefinition);
            }

            foreach (var local in locals)
            {
                processor.Emit(OpCodes.Ldloc, local);
            }

            processor.Emit(OpCodes.Callvirt, methodDefinition);
            processor.Emit(OpCodes.Ret);

            typeDefinition.Methods.Add(handler);
            return handler;
        }

        private void EmitRegistrationInitializer(ModuleDefinition mainModule, List<(uint Hash, MethodDefinition Handler, string Name)> registrations)
        {
            var initializerType = new TypeDefinition("__GEN", "VaporRpcInitializer",
                TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.AnsiClass | TypeAttributes.BeforeFieldInit,
                mainModule.TypeSystem.Object);

            var registerMethod = new MethodDefinition("RegisterRpcs",
                MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig,
                mainModule.TypeSystem.Void);

            var attribute = new CustomAttribute(m_RuntimeInitializeOnLoadAttribute_CtorRef);
            attribute.ConstructorArguments.Add(new CustomAttributeArgument(m_RuntimeInitializeLoadType_TypeRef, k_SubsystemRegistrationLoadType));
            registerMethod.CustomAttributes.Add(attribute);

            var processor = registerMethod.Body.GetILProcessor();
            foreach (var (hash, handler, name) in registrations)
            {
                processor.Emit(OpCodes.Ldc_I4, unchecked((int)hash));
                processor.Emit(OpCodes.Ldnull);
                processor.Emit(OpCodes.Ldftn, handler);
                processor.Emit(OpCodes.Newobj, m_RpcHandlerDelegateCtor_MethodRef);
                processor.Emit(OpCodes.Ldstr, name);
                processor.Emit(OpCodes.Call, m_RegisterRpc_MethodRef);
            }

            processor.Emit(OpCodes.Ret);

            initializerType.Methods.Add(registerMethod);
            mainModule.Types.Add(initializerType);
        }

        #endregion
    }
}
