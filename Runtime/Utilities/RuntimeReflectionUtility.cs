using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Assertions;

namespace Vapor
{
    public static class RuntimeReflectionUtility
    {
        public static MethodInfo GetMethodInfo(Type declaringType, string methodName, string[] parameterTypes)
        {
            if (declaringType == null)
            {
                return null;
            }

            var possibleMethods = declaringType.GetMethods().Where(m => m.Name == methodName).Where(m => m.GetParameters().Length == parameterTypes.Length).ToArray();
            if (possibleMethods.Length == 0)
            {
                Debug.LogError($"Method '{methodName}' not found");
                return null;
            }
            
            
            if (possibleMethods.Length == 1)
            {
                return possibleMethods[0];
            }
            else
            {
                bool isGeneric = false;
                var paramTypes = new Type[parameterTypes.Length];
                var paramTypeNames = new string[parameterTypes.Length];
                for (int i = 0; i < parameterTypes.Length; i++)
                {
                    paramTypes[i] = Type.GetType(parameterTypes[i]);
                    paramTypeNames[i] = paramTypes[i]?.Name ?? parameterTypes[i];
                    if (paramTypes[i] == null && !isGeneric)
                    {
                        isGeneric = true;
                    }
                }

                if(isGeneric)
                {
                    foreach (var mi in possibleMethods)
                    {
                        if (!mi.IsGenericMethodDefinition)
                        {
                            continue;
                        }

                        bool matches = true;
                        int idx = 0;
                        foreach (var pi in mi.GetParameters())
                        {
                            if (pi.ParameterType.Name != paramTypeNames[idx])
                            {
                                matches = false;
                                break;
                            }
                            idx++;
                        }

                        if (matches)
                        {
                            return mi;
                        }
                    }
                    Debug.LogError($"Method '{methodName}' not found");
                    return null;
                }
                else
                {
                    return declaringType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, paramTypes, null);
                }
            }
        }

        public static FieldInfo GetFieldInfo(Type declaringType, string fieldName)
        {
            return declaringType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }
        
        public static PropertyInfo GetPropertyInfo(Type declaringType, string fieldName)
        {
            return declaringType.GetProperty(fieldName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }
    }
}