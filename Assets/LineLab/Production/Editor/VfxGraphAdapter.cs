using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace LineLab.Production.Editor
{
    /// <summary>
    /// The only adapter to Unity's internal VFX authoring model. Runtime code uses
    /// public APIs. Keep version-sensitive reflection here, outside content recipes.
    /// </summary>
    internal static class VfxGraphAdapter
    {
        const BindingFlags Flags =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static;

        public static Type Type(string name)
        {
            var type = AppDomain
                .CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(name))
                .FirstOrDefault(value => value != null);
            return type
                ?? throw new NotSupportedException(
                    "The installed VFX Graph package does not expose "
                        + name
                        + ". Update VfxGraphAdapter for this package version."
                );
        }

        public static object Call(object target, string method, params object[] args)
        {
            var type = target as Type ?? target.GetType();
            foreach (var candidate in type.GetMethods(Flags).Where(value => value.Name == method))
            {
                var parameters = candidate.GetParameters();
                if (
                    parameters.Length < args.Length
                    || !parameters.Skip(args.Length).All(value => value.IsOptional)
                )
                {
                    continue;
                }

                if (
                    args.Where(
                            (value, index) =>
                                value != null
                                && !parameters[index].ParameterType.IsInstanceOfType(value)
                        )
                        .Any()
                )
                {
                    continue;
                }

                try
                {
                    return candidate.Invoke(
                        target is Type ? null : target,
                        args.Concat(
                                parameters.Skip(args.Length).Select(value => value.DefaultValue)
                            )
                            .ToArray()
                    );
                }
                catch (TargetInvocationException exception)
                {
                    throw exception.InnerException ?? exception;
                }
            }

            throw new NotSupportedException(
                "VFX Graph authoring API changed: " + type.Name + "." + method
            );
        }

        public static object Get(object target, string name)
        {
            var property = target
                .GetType()
                .GetProperties(Flags)
                .FirstOrDefault(value =>
                    value.Name == name && value.GetIndexParameters().Length == 0
                );
            if (property != null)
            {
                return property.GetValue(target);
            }

            var field = target.GetType().GetField(name, Flags);
            if (field != null)
            {
                return field.GetValue(target);
            }

            throw new MissingMemberException(target.GetType().FullName, name);
        }

        public static void Set(object target, string name, object value)
        {
            var property = target.GetType().GetProperty(name, Flags);
            if (property != null)
            {
                property.SetValue(target, value);
                return;
            }

            var field = target.GetType().GetField(name, Flags);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }

            throw new MissingMemberException(target.GetType().FullName, name);
        }

        public static object Node(string name)
        {
            return ScriptableObject.CreateInstance(Type("UnityEditor.VFX." + name));
        }

        public static void Setting(object target, string name, object value)
        {
            Call(target, "SetSettingValue", name, value);
        }

        public static object Slot(object target, string name)
        {
            return ((IEnumerable)Get(target, "inputSlots"))
                .Cast<object>()
                .First(slot =>
                    (string)Get(slot, "name") == name || (string)Get(slot, "name") == "_" + name
                );
        }

        public static void Value(object target, string name, object value)
        {
            Set(Slot(target, name), "value", value);
        }

        public static void Connect(object target, string name, object parameter)
        {
            Call(Slot(target, name), "Link", Call(parameter, "GetOutputSlot", 0));
        }

        public static object Parameter(
            object graph,
            string name,
            Type type,
            object initial,
            Vector2 position
        )
        {
            var parameter = Node("VFXParameter");
            Call(parameter, "Init", type);
            Setting(parameter, "m_ExposedName", name);
            Setting(parameter, "m_Exposed", true);
            if (initial != null)
            {
                Set(parameter, "value", initial);
            }

            Set(parameter, "position", position);
            Call(graph, "AddChild", parameter);
            Call(parameter, "AddNode", position);
            return parameter;
        }

        public static void EnsureFolder(string path)
        {
            var parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
