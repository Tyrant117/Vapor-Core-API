using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace VaporEditor.Inspector
{
    public partial class InspectorTreeObject
    {
        private static readonly HashSet<InspectorTreeObject> s_PendingApply = new();
        private static readonly List<InspectorTreeObject> s_FlushBuffer = new();
        private static bool s_FlushScheduled;

        // Anything that is about to read the serialized state gets the pending writes first, so a
        // deferred flush can never be the reason an edit did not make it to disk. Three of the four
        // points are attributes now; only scene saving still has no lifecycle equivalent, and it is
        // the only reason this hook survives.
        [OnCodeInitializing]
        private static void HookFlushPoints()
        {
            EditorSceneManager.sceneSaving -= OnSceneSaving;
            EditorSceneManager.sceneSaving += OnSceneSaving;
        }

        /// <summary>Replaces <c>AssemblyReloadEvents.beforeAssemblyReload</c>.</summary>
        [OnCodeUnloading]
        private static void FlushBeforeCodeUnload() => FlushPendingApplies();

        /// <summary>
        /// The two transitions that used to arrive through <c>playModeStateChanged</c>.
        /// </summary>
        /// <remarks>
        /// Only the exiting halves are hooked. The entering ones fired immediately after, with no
        /// opportunity for an edit in between, so they could never have had anything left to flush.
        /// </remarks>
        [OnExitingEditMode]
        private static void FlushBeforeEnteringPlayMode() => FlushPendingApplies();

        /// <inheritdoc cref="FlushBeforeEnteringPlayMode"/>
        [OnExitingPlayMode]
        private static void FlushBeforeLeavingPlayMode() => FlushPendingApplies();

        private static void OnSceneSaving(Scene scene, string path)
        {
            FlushPendingApplies();
        }

        // Implicit conversion from SerializedInspectorObject to UnityEngine.Object
        public static implicit operator Object(InspectorTreeObject serializedInspectorObject)
        {
            return serializedInspectorObject.IsUnityObject ? serializedInspectorObject._serializedObject.targetObject : null;
        }

        public bool IsUnityObject { get; }
        private readonly object _internalObject;
        public object Object => IsUnityObject ? _serializedObject?.targetObject : _internalObject;
        public SerializedObject SerializedObject => IsUnityObject ? _serializedObject : null;
        public Type Type { get; }
        public InspectorTreeObject ParentObject { get; private set; }

        private readonly SerializedObject _serializedObject;
        private List<InspectorTreeProperty> _fields;
        private List<InspectorTreeProperty> _methods;
        private List<InspectorTreeProperty> _properties;
        private Dictionary<string, InspectorTreeProperty> _map;

        public List<InspectorTreeProperty> Fields => _fields;
        public List<InspectorTreeProperty> Methods => _methods;
        public List<InspectorTreeProperty> Properties => _properties;

        public InspectorTreeObject(SerializedObject serializedObject)
        {
            IsUnityObject = true;
            Type = serializedObject.targetObject.GetType();

            _serializedObject = serializedObject;

            BuildSerializedInspectorProperties();
        }

        public InspectorTreeObject(object @object, Type type)
        {
            IsUnityObject = false;
            _internalObject = @object;
            Type = type;

            BuildSerializedInspectorProperties();
        }

        public InspectorTreeObject WithParent(InspectorTreeObject parentObject)
        {
            ParentObject = parentObject;
            return this;
        }

        /// <summary>
        /// Builds only this object's own members. Their children are materialized on demand as the visual
        /// tree descends into them, so nothing below the top level is reflected over until it is drawn.
        /// </summary>
        private void BuildSerializedInspectorProperties()
        {
            var typeStack = new Stack<Type>();
            for (var targetType = Type; targetType != null; targetType = targetType.BaseType)
            {
                typeStack.Push(targetType);
            }

            // Gathered base-first and kept in member kind order, because equal draw orders are sorted
            // stably later on and this is what decides the default layout.
            var fieldInfos = new List<FieldInfo>();
            var propertyInfos = new List<PropertyInfo>();
            var methodInfos = new List<MethodInfo>();
            while (typeStack.TryPop(out var type))
            {
                fieldInfos.AddRange(type.GetFields(InspectorTreeProperty.MemberSearchFlags));
                propertyInfos.AddRange(type.GetProperties(InspectorTreeProperty.MemberSearchFlags));
                methodInfos.AddRange(type.GetMethods(InspectorTreeProperty.MemberSearchFlags));
            }

            _map = new Dictionary<string, InspectorTreeProperty>(fieldInfos.Count + methodInfos.Count + propertyInfos.Count);

            _fields = new List<InspectorTreeProperty>(fieldInfos.Count);
            foreach (var field in fieldInfos)
            {
                if (!ReflectionUtility.FieldSearchPredicate(field))
                {
                    continue;
                }

                var prop = new InspectorTreeProperty(this, null, field, field.Name);
                _fields.Add(prop);
                AddToMap(field.Name, prop);
            }

            _methods = new List<InspectorTreeProperty>();
            foreach (var method in methodInfos)
            {
                if (!ReflectionUtility.MethodSearchPredicate(method))
                {
                    continue;
                }

                var prop = new InspectorTreeProperty(this, null, method, method.Name);
                _methods.Add(prop);
                AddToMap(method.Name, prop);
            }

            _properties = new List<InspectorTreeProperty>();
            foreach (var property in propertyInfos)
            {
                // A [VslSerialize] property is stored state with a setter, so it is drawn as an
                // editable field rather than as a read-only computed row. Sorting it into _fields is
                // all that takes: the field element is chosen by which list a member lands in.
                var editable = ReflectionUtility.IsVslSerialized(property);
                if (!editable && !ReflectionUtility.PropertySearchPredicate(property))
                {
                    continue;
                }

                var prop = new InspectorTreeProperty(this, null, property, property.Name);
                (editable ? _fields : _properties).Add(prop);
                AddToMap(property.Name, prop);
            }
        }

        public InspectorTreeProperty FindProperty(string propertyPath)
        {
            return _map.TryGetValue(propertyPath, out var prop) ? prop : null;
        }

        public SerializedProperty FindSerializedProperty(string propertyPath)
        {
            //Debug.Log($"FindSerializedProperty: {propertyPath}");
            return IsUnityObject ? _serializedObject.FindProperty(propertyPath) : null;
        }

        /// <summary>
        /// Queues this object - and the chain it belongs to - to be pushed back through its SerializedObject.
        /// <para>
        /// Coalesced to one flush per editor tick rather than running inline. Every keystroke calls this, and
        /// a full Update + ApplyModifiedProperties round trip per character is the single most expensive
        /// thing in the edit path. Values are written to the C# object immediately either way; this only
        /// defers the serialization sync and the dirty flag.
        /// </para>
        /// </summary>
        public void ApplyModifiedProperties()
        {
            for (var target = this; target != null; target = target.ParentObject)
            {
                s_PendingApply.Add(target);
            }

            if (s_FlushScheduled)
            {
                return;
            }

            s_FlushScheduled = true;
            EditorApplication.delayCall += FlushPendingApplies;
        }

        /// <summary>
        /// Applies immediately instead of waiting for the tick. For the points where the editor is about to
        /// read the serialized state itself - entering play mode, saving, reloading scripts.
        /// </summary>
        public static void FlushPendingApplies()
        {
            s_FlushScheduled = false;
            if (s_PendingApply.Count == 0)
            {
                return;
            }

            // Copied out first: applying can destroy an object, and a destroyed one re-queues nothing.
            s_FlushBuffer.Clear();
            s_FlushBuffer.AddRange(s_PendingApply);
            s_PendingApply.Clear();

            foreach (var target in s_FlushBuffer)
            {
                if (!target.IsUnityObject || !(Object)target.Object)
                {
                    continue;
                }

                target._serializedObject.Update();
                target._serializedObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty((Object)target.Object);
            }

            s_FlushBuffer.Clear();
        }

        #region - Attributes -
        public bool HasAttribute<T>() where T : Attribute
        {
            return Type.IsDefined(typeof(T), true);
        }

        public bool TryGetAttribute<T>(out T atr) where T : Attribute
        {
            atr = Type.GetCustomAttribute<T>(true);
            return atr != null;
        }

        public bool TryGetAttributes<T>(out IEnumerable<T> atr) where T : Attribute
        {
            atr = Type.GetCustomAttributes<T>(true);
            return atr != null;
        }

        internal void AddToMap(string path, InspectorTreeProperty prop)
        {
            //Debug.Log($"Map Property At {path}");
            _map[path] = prop;
        }
        #endregion
    }
}
