﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using NLog;
using Torch.API;
using Torch.API.Plugins;
using Torch.Commands.Permissions;
using VRage.Game.ModAPI;

namespace Torch.Commands
{
    public class Command
    {
        public MyPromoteLevel MinimumPromoteLevel { get; }
        public string Name { get; }
        public string Description { get; }
        public string HelpText { get; }
        public Type Module { get; }
        public List<string> Path { get; } = new List<string>();
        public ITorchPlugin Plugin { get; }
        public string SyntaxHelp { get; }

        private readonly MethodInfo _method;
        private ParameterInfo[] _parameters;
        private int? _requiredParamCount;

        public Command(ITorchPlugin plugin, MethodInfo commandMethod)
        {
            Plugin = plugin;

            var commandAttribute = commandMethod.GetCustomAttribute<CommandAttribute>();
            if (commandAttribute == null)
                throw new TypeLoadException($"Method does not have a {nameof(CommandAttribute)}");

            var permissionAttribute = commandMethod.GetCustomAttribute<PermissionAttribute>();
            MinimumPromoteLevel = permissionAttribute?.PromoteLevel ?? MyPromoteLevel.None;

            if (!commandMethod.DeclaringType.IsSubclassOf(typeof(CommandModule)))
                throw new TypeLoadException($"Command {commandMethod.Name}'s declaring type {commandMethod.DeclaringType.FullName} is not a subclass of {nameof(CommandModule)}");

            var moduleAttribute = commandMethod.DeclaringType.GetCustomAttribute<CategoryAttribute>();

            _method = commandMethod;
            Module = commandMethod.DeclaringType;

            if (moduleAttribute != null)
            {
                Path.AddRange(moduleAttribute.Path);
            }
            Path.AddRange(commandAttribute.Path);

            Name = commandAttribute.Name;
            Description = commandAttribute.Description;
            HelpText = commandAttribute.HelpText;

            //parameters
            _parameters = commandMethod.GetParameters();

            var sb = new StringBuilder();
            sb.Append($"/{string.Join(" ", Path)} ");
            for (var i = 0; i < _parameters.Length; i++)
            {
                var param = _parameters[i];

                if (param.HasDefaultValue)
                {
                    _requiredParamCount = _requiredParamCount ?? i;

                    sb.Append($"[{param.ParameterType.Name} {param.Name}] ");
                }
                else
                {
                    sb.Append($"<{param.ParameterType.Name} {param.Name}> ");
                }
            }

            _requiredParamCount = _requiredParamCount ?? _parameters.Length;
            LogManager.GetLogger(nameof(Command)).Debug($"Params: {_parameters.Length} ({_requiredParamCount} required)");
            SyntaxHelp = sb.ToString();
        }

        public bool TryInvoke(CommandContext context)
        {
            var parameters = new object[_parameters.Length];

            if (context.Args.Count < _requiredParamCount)
                return false;

            //Convert args from string
            for (var i = 0; i < _parameters.Length && i < context.Args.Count; i++)
            {
                if (context.Args[i].TryConvert(_parameters[i].ParameterType, out object obj))
                    parameters[i] = obj;
                else
                    return false;
            }

            //Fill remaining parameters with default values
            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameters[i] == null)
                    parameters[i] = _parameters[i].DefaultValue;
            }

            var moduleInstance = (CommandModule)Activator.CreateInstance(Module);
            moduleInstance.Context = context;
            _method.Invoke(moduleInstance, parameters);
            return true;
        }
    }

    public static class Extensions
    {
        public static bool TryConvert(this string str, Type toType, out object val)
        {
            try
            {
                var converter = TypeDescriptor.GetConverter(toType);
                val = converter.ConvertFromString(str);
                return true;
            }
            catch (NotSupportedException)
            {
                val = null;
                return false;
            }
        }

        public static bool TryConvert<T>(this string str, out T val)
        {
            try
            {
                var converter = TypeDescriptor.GetConverter(typeof(T));
                val = (T)converter.ConvertFromString(str);
                return true;
            }
            catch (NotSupportedException)
            {
                val = default(T);
                return false;
            }

        }
    }
}