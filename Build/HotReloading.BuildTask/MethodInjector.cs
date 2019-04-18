﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HotReloading.BuildTask.Extensions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace HotReloading.BuildTask
{
    public class MethodInjector : Task
    {
        public MethodInjector()
        {
            Logger = new Logger(Log);
        }

        public MethodInjector(ILogger logger)
        {
            Logger = logger;
        }

        public ILogger Logger { get; }

        [Required] public string AssemblyFile { get; set; }

        [Required] public string ProjectDirectory { get; set; }
        [Required]
        public string References { get; set; }

        public bool DebugSymbols { get; set; }
        public string DebugType { get; set; }

        public override bool Execute()
        {
            var assemblyPath = Path.Combine(ProjectDirectory, AssemblyFile);
            InjectCode(assemblyPath, assemblyPath);

            Logger.LogMessage("Injection done");
            return true;
        }

        public void InjectCode(string assemblyPath, string outputAssemblyPath)
        {
            var debug = DebugSymbols || !string.IsNullOrEmpty(DebugType) && DebugType.ToLowerInvariant() != "none";

            var assemblyReferenceResolver = new AssemblyReferenceResolver();

            if (!string.IsNullOrEmpty(References))
            {
                var paths = References.Replace("//", "/").Split(';').Distinct();
                foreach (var p in paths)
                {
                    var searchpath = Path.GetDirectoryName(p);
                    Logger.LogMessage($"Adding searchpath {searchpath}");
                    assemblyReferenceResolver.AddSearchDirectory(searchpath);
                }
            }

            var ad = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters
            {
                ReadWrite = true,
                ReadSymbols = true,
                AssemblyResolver = assemblyReferenceResolver
            });

            var md = ad.MainModule;

            var test1 = md.AssemblyReferences;

            var types = md.Types.Where(x => x.BaseType != null).ToList();

            var iInstanceClassType = md.ImportReference(typeof(IInstanceClass));

            foreach (var type in types)
            {
                var methods = type.Methods;

                MethodDefinition getInstanceMethod = null;
                MethodDefinition instanceMethodGetters = null;

                if (type.IsDelegate(md))
                    continue;

                var hasImplementedIInstanceClass = type.HasImplementedIInstanceClass(iInstanceClassType, out getInstanceMethod, out instanceMethodGetters);

                ImplementIInstanceClass(md, type, ref getInstanceMethod, ref instanceMethodGetters, hasImplementedIInstanceClass);

                foreach (var method in methods)
                {
                    if (method.CustomAttributes
                            .Any(x => x.AttributeType.Name == "CompilerGeneratedAttribute") ||
                        method.CustomAttributes
                            .Any(x => x.AttributeType.Name == "GeneratedCodeAttribute"))
                        continue;

                    if (method == instanceMethodGetters || method.IsConstructor || method == getInstanceMethod)
                        continue;

                    Logger.LogMessage("Weaving Method " + method.Name);

                    WrapMethod(md, type, method, getInstanceMethod.GetReference(md));
                }

                AddOverrideMethod(type, md, getInstanceMethod.GetReference(md));
            }

            if (assemblyPath == outputAssemblyPath)
            {
                ad.Write(new WriterParameters
                {
                    WriteSymbols = debug
                });
            }
            else
            {
                ad.Write(outputAssemblyPath, new WriterParameters
                {
                    WriteSymbols = debug
                });
            }

            ad.Dispose();
        }

        private void AddOverrideMethod(TypeDefinition type, ModuleDefinition md, MethodReference getInstanceMethod)
        {
            if (type.BaseType == null)
                return;

            var overridableMethods = GetOverridableMethods(type, md);

            foreach(var overridableMethod in overridableMethods)
            {
                MethodReference overridableReference = null;
                try
                {
                    var baseMethod = GetBaseMethod(overridableMethod);
                    overridableReference = md.ImportReference(baseMethod);
                }
                catch(Exception ex)
                {

                }
                if (!type.Methods.Any(x => AreEquals(x, overridableMethod.Method)))
                {
                    //var attributes = overridableMethod.Attributes & ~MethodAttributes.NewSlot | MethodAttributes.ReuseSlot;
                    //var method = new MethodDefinition(overridableMethod.Name, attributes, md.ImportReference(typeof(void)));
                    //method.ImplAttributes = overridableMethod.ImplAttributes;
                    //method.SemanticsAttributes = overridableMethod.SemanticsAttributes;

                    //foreach(var genericParameter in overridableMethod.GenericParameters)
                    //{
                    //    if (genericParameter.Type == GenericParameterType.Method)
                    //    {
                    //        method.GenericParameters.Add(new GenericParameter(genericParameter.Name, method));
                    //    }
                    //}

                    //TypeReference returnType;

                    //if(overridableMethod.ReturnType is GenericParameter genericReturn)
                    //{
                    //    if(genericReturn.Type == GenericParameterType.Method)
                    //    {
                    //        returnType = method.GenericParameters.First(x => x.Name == genericReturn.Name);
                    //    }
                    //    else
                    //    {
                    //        returnType = type.GenericParameters.FirstOrDefault(x => x.Name == genericReturn.Name);

                    //        if (returnType == null && type.BaseType is GenericInstanceType genericInstanceType)
                    //        {
                    //        }
                    //    }
                    //}
                    //else
                    //{
                    //    returnType = md.ImportReference(overridableMethod.ReturnType);
                    //}

                    //method.ReturnType = returnType;

                    //foreach (var parameter in overridableMethod.Parameters)
                    //{
                    //    TypeReference parameterType;
                    //    if(parameter.ParameterType is GenericParameter genericParameter)
                    //    {
                    //        if (genericParameter.Type == GenericParameterType.Method)
                    //        {
                    //            parameterType = method.GenericParameters.First(x => x.Name == genericParameter.Name);
                    //        }
                    //        else
                    //        {
                    //            parameterType = type.GenericParameters.First(x => x.Name == genericParameter.Name);
                    //        }
                    //    }
                    //    else
                    //    {
                    //        parameterType = md.ImportReference(parameter.ParameterType);
                    //    }

                    //    method.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, parameterType));
                    //}

                    var method = overridableMethod.Method;

                    var returnType = method.ReturnType;

                    var composer = new InstructionComposer(md);

                    composer.LoadArg_0();

                    for(int i =0;i<method.Parameters.Count;i++)
                    {
                        composer.LoadArg(method.Parameters[i]);
                        //overridableReference.Parameters[i].ParameterType = method.Parameters[i].ParameterType;
                    }

                    if (type.BaseType.IsGenericInstance)
                    {
                        var baseTypeInstance = (GenericInstanceType)type.BaseType;
                        overridableReference = overridableReference.MakeGeneric(baseTypeInstance.GenericArguments.ToArray());
                    }
                    else
                    {

                    }

                    composer.BaseCall(overridableReference);

                    if (overridableMethod.Method.ReturnType.FullName != "System.Void")
                    {
                        var returnVariable = new VariableDefinition(returnType);

                        method.Body.Variables.Add(returnVariable);

                        composer.Store(returnVariable);
                        composer.Load(returnVariable);
                    }

                    composer.Return();

                    foreach(var instruction in composer.Instructions)
                    {
                        method.Body.GetILProcessor().Append(instruction);
                    }

                    WrapMethod(md, type, method, getInstanceMethod);

                    type.Methods.Add(method);
                }
            }
        }

        public class OverridableMethod
        {
            public MethodDefinition Method;
            public OverridableMethod BaseMethod;

        }
        private IEnumerable<OverridableMethod> GetOverridableMethods(TypeDefinition type, ModuleDefinition md)
        {
            if (type.BaseType == null)
                return null;

            var retVal = new List<OverridableMethod>();

            var overriadableMethods = new List<MethodDefinition>();

            var baseTypeDefinition = type.BaseType.Resolve();

            var test = baseTypeDefinition.Methods.Where(x => x.IsVirtual && !x.IsFinal && x.Name != "Finalize");

            foreach(var method in test)
            {
                retVal.Add(CopyMethod(type, new OverridableMethod{ Method = method}, md));
            }

            //Ignore non virtual, sealed, finalized and generic methods
            //overriadableMethods.AddRange(test);

            var baseOverriableMethods = GetOverridableMethods(baseTypeDefinition, md);
            if (baseOverriableMethods == null)
                return retVal;

            //overriadableMethods.AddRange(baseOverriableMethods.Select(x => x.Method).Where(x => !overriadableMethods.Any(y => AreEquals(x, y))));

            foreach(var method in baseOverriableMethods)
            {
                retVal.Add(CopyMethod(type, method, md));
            }

            //retVal.AddRange(overriadableMethods.Select(x => new OverridableMethod { Method = x, Type = baseTypeDefinition });

            return retVal;
        }

        private OverridableMethod CopyMethod(TypeDefinition type, OverridableMethod overridableMethod, ModuleDefinition md)
        {
            var attributes = overridableMethod.Method.Attributes & ~MethodAttributes.NewSlot | MethodAttributes.ReuseSlot;
            var method = new MethodDefinition(overridableMethod.Method.Name, attributes, md.ImportReference(typeof(void)));
            method.ImplAttributes = overridableMethod.Method.ImplAttributes;
            method.SemanticsAttributes = overridableMethod.Method.SemanticsAttributes;
            method.DeclaringType = type;

            foreach (var genericParameter in overridableMethod.Method.GenericParameters)
            {
                if (genericParameter.Type == GenericParameterType.Method)
                {
                    method.GenericParameters.Add(new GenericParameter(genericParameter.Name, method));
                }
            }

            TypeReference returnType;

            if (overridableMethod.Method.ReturnType is GenericParameter genericReturn)
            {
                if (genericReturn.Type == GenericParameterType.Method)
                {
                    returnType = method.GenericParameters.First(x => x.Name == genericReturn.Name);
                }
                else
                {
                    if (type.BaseType is GenericInstanceType genericInstanceType)
                    {
                        var genericArgument = genericInstanceType.GenericArguments[genericReturn.Position];
                        if(genericArgument.IsGenericParameter)
                        {
                            returnType = genericArgument;
                        }
                        else
                        {
                            returnType = md.ImportReference(genericArgument);
                        }
                    }
                    else
                        returnType = type.GenericParameters.FirstOrDefault(x => x.Name == genericReturn.Name);
                }
            }
            else
            {
                returnType = md.ImportReference(overridableMethod.Method.ReturnType);
            }

            method.ReturnType = returnType;

            foreach (var parameter in overridableMethod.Method.Parameters)
            {
                TypeReference parameterType;
                if (parameter.ParameterType is GenericParameter genericParameter)
                {
                    if (genericParameter.Type == GenericParameterType.Method)
                    {
                        parameterType = method.GenericParameters.First(x => x.Name == genericParameter.Name);
                    }
                    else
                    {
                        if (type.BaseType is GenericInstanceType genericInstanceType)
                        {
                            var genericArgument = genericInstanceType.GenericArguments[genericParameter.Position];
                            if (genericArgument.IsGenericParameter)
                            {
                                parameterType = genericArgument;
                            }
                            else
                            {
                                parameterType = md.ImportReference(genericArgument);
                            }
                        }
                        else
                            parameterType = type.GenericParameters.FirstOrDefault(x => x.Name == genericParameter.Name);
                    }
                }
                else
                {
                    parameterType = md.ImportReference(parameter.ParameterType);
                }

                method.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, parameterType));
            }


            return new OverridableMethod
            {
                Method = method,
                    BaseMethod = overridableMethod 
            };
        }

        private MethodDefinition GetBaseMethod(OverridableMethod overridableMethod)
        {
            if (overridableMethod.BaseMethod != null)
                return GetBaseMethod(overridableMethod.BaseMethod);

            return overridableMethod.Method;
        }

        private static bool AreEquals(MethodDefinition method1, MethodDefinition method2)
        {
            if (method1.Name != method2.Name)
                return false;

            if (method1.Parameters.Count != method2.Parameters.Count)
                return false;

            for (var i = 0; i < method1.Parameters.Count; i++)
            {
                if (method1.Parameters[i].ParameterType.FullName != method2.Parameters[i].ParameterType.FullName)
                    return false;
            }

            return true;
        }

        private static void ImplementIInstanceClass(ModuleDefinition md, TypeDefinition type, ref MethodDefinition getInstanceMethod, ref MethodDefinition instanceMethodGetters, bool hasImplementedIInstanceClass)
        {
            if (!type.IsAbstract && !hasImplementedIInstanceClass)
            {
                type.Interfaces.Add(new InterfaceImplementation(md.ImportReference(typeof(IInstanceClass))));
                PropertyDefinition instanceMethods = CreateInstanceMethodsProperty(md, type, out instanceMethodGetters);
                getInstanceMethod = CreateGetInstanceMethod(md, instanceMethods.GetMethod.GetReference(md), type, hasImplementedIInstanceClass);
            }
        }

        private static PropertyDefinition CreateInstanceMethodsProperty(ModuleDefinition md, TypeDefinition type, out MethodDefinition instanceMethodGetters)
        {
            var instaceMethodType = md.ImportReference(typeof(Dictionary<string, Delegate>));
            var field = type.CreateField(md, "instanceMethods", instaceMethodType);
            var fieldReference = field.GetReference();

            var returnComposer = new InstructionComposer(md)
                .Load_1()
                .Return();

            var getFieldComposer = new InstructionComposer(md)
                .LoadArg_0()
                .Load(fieldReference)
                .Store_1()
                .MoveTo(returnComposer.Instructions.First());

            var initializeField = new InstructionComposer(md)
                .NoOperation()
                .LoadArg_0()
                .LoadArg_0()
                .StaticCall(new Method
                {
                    ParentType = typeof(CodeChangeHandler),
                    MethodName = nameof(CodeChangeHandler.GetInitialInstanceMethods),
                    ParameterSignature = new[] { typeof(IInstanceClass) }
                })
                .Store(fieldReference)
                .NoOperation();

            var getterComposer = new InstructionComposer(md);
            getterComposer.LoadArg_0()
                .Load(fieldReference)
                .LoadNull()
                .CompareEqual()
                .Store_0()
                .Load_0()
                .MoveToWhenFalse(getFieldComposer.Instructions.First())
                .Append(initializeField)
                .Append(getFieldComposer)
                .Append(returnComposer);

            instanceMethodGetters = type.CreateGetter(md, "InstanceMethods", instaceMethodType, getterComposer.Instructions, vir: true);

            var boolVariable = new VariableDefinition(md.ImportReference(typeof(bool)));
            instanceMethodGetters.Body.Variables.Add(boolVariable);

            var delegateVariable = new VariableDefinition(md.ImportReference(typeof(Delegate)));
            instanceMethodGetters.Body.Variables.Add(delegateVariable);

            return type.CreateProperty("InstanceMethods", instaceMethodType, instanceMethodGetters, null);
        }

        private static void WrapMethod(ModuleDefinition md, TypeDefinition type, MethodDefinition method, MethodReference getInstanceMethod)
        {
            var methodKeyVariable = new VariableDefinition(md.ImportReference(typeof(string)));
            method.Body.Variables.Add(methodKeyVariable);

            var delegateVariable = new VariableDefinition(md.ImportReference(typeof(Delegate)));
            method.Body.Variables.Add(delegateVariable);

            var boolVariable = new VariableDefinition(md.ImportReference(typeof(bool)));
            method.Body.Variables.Add(boolVariable);

            var instructions = method.Body.Instructions;
            var ilprocessor = method.Body.GetILProcessor();
            Instruction loadInstructionForReturn = null;
            if (method.ReturnType.FullName != "System.Void" && instructions.Count > 1)
            {
                var secondLastInstruction =  instructions.ElementAt(instructions.Count - 2);

                if (IsLoadInstruction(secondLastInstruction))
                    loadInstructionForReturn = secondLastInstruction;
            }

            var retInstruction = instructions.Last();

            var oldInstruction = method.Body.Instructions.Where(x => x != retInstruction &&
                                                                    x != loadInstructionForReturn).ToList();

            var lastInstruction = retInstruction;

            method.Body.Instructions.Clear();

            if (loadInstructionForReturn != null)
            {
                method.Body.Instructions.Add(loadInstructionForReturn);
                lastInstruction = loadInstructionForReturn;
            }

            method.Body.Instructions.Add(retInstruction);

            foreach (var instruction in oldInstruction) ilprocessor.InsertBefore(lastInstruction, instruction);
            method.Body.InitLocals = true;
            var firstInstruction = method.Body.Instructions.First();

            var parameters = method.Parameters.ToArray();

            var composer = new InstructionComposer(md);

            if(method.IsStatic)
                ComposeStaticMethodInstructions(type, method, delegateVariable, boolVariable, methodKeyVariable, firstInstruction, parameters, composer);
            else
                ComposeInstanceMethodInstructions(type, method, delegateVariable, boolVariable, methodKeyVariable, firstInstruction, parameters, composer, getInstanceMethod);

            if (method.ReturnType.FullName == "System.Void") composer.Pop();
            else if (method.ReturnType.IsValueType)
            {
                composer.Unbox_Any(method.ReturnType);
            }
            else
            {
                composer.Cast(method.ReturnType);
            }

            if (loadInstructionForReturn != null)
            {
                Instruction storeInstructionForReturn = GetStoreInstruction(loadInstructionForReturn);
                composer.Append(storeInstructionForReturn);
            }

            composer.MoveTo(lastInstruction);

            foreach (var instruction in composer.Instructions) ilprocessor.InsertBefore(firstInstruction, instruction);
        }

        private static void ComposeInstanceMethodInstructions(TypeDefinition type, MethodDefinition method, VariableDefinition delegateVariable, VariableDefinition boolVariable, VariableDefinition methodKeyVariable, Instruction firstInstruction, ParameterDefinition[] parameters, InstructionComposer composer, MethodReference getInstanceMethod)
        {
            composer
            .Load(method.Name)
                .LoadArray(parameters.Length, typeof(string), parameters.Select(x => x.ParameterType.FullName).ToArray())
                .StaticCall(new Method
                {
                    ParentType = typeof(CodeChangeHandler),
                    MethodName = nameof(CodeChangeHandler.GetMethodKey),
                    ParameterSignature = new[] { typeof(string), typeof(string[]) }
                }).
                Store(methodKeyVariable)
                .LoadArg_0()
                .Load(methodKeyVariable)
                .InstanceCall(getInstanceMethod)
                .Store(delegateVariable)
                .Load(delegateVariable)
                .IsNotNull()
                .Store(boolVariable)
                .Load(boolVariable)
                .MoveToWhenFalse(firstInstruction)
                .NoOperation()
                .Load(delegateVariable)
                .LoadArray(parameters, true)
                .InstanceCall(new Method
                {
                    ParentType = typeof(Delegate),
                    MethodName = nameof(Delegate.DynamicInvoke),
                    ParameterSignature = new[] { typeof(object[]) }
                });
        }

        private static void ComposeStaticMethodInstructions(TypeDefinition type, MethodDefinition method, VariableDefinition delegateVariable, VariableDefinition boolVariable, VariableDefinition methodKeyVariable, Instruction firstInstruction, ParameterDefinition[] parameters, InstructionComposer composer)
        {
            composer
            .Load(method.Name)
                .LoadArray(parameters.Length, typeof(string), parameters.Select(x => x.ParameterType.FullName).ToArray())
                .StaticCall(new Method
                {
                    ParentType = typeof(CodeChangeHandler),
                    MethodName = nameof(CodeChangeHandler.GetMethodKey),
                    ParameterSignature = new[] { typeof(string), typeof(string[])}
                }).
                Store(methodKeyVariable)
                .Load(type)
                            .StaticCall(new Method
                            {
                                ParentType = typeof(Type),
                                MethodName = nameof(Type.GetTypeFromHandle),
                                ParameterSignature = new[] { typeof(RuntimeTypeHandle) }
                            })
                            .Load(methodKeyVariable)
                            .StaticCall(new Method
                            {
                                ParentType = typeof(CodeChangeHandler),
                                MethodName = nameof(CodeChangeHandler.GetMethodDelegate),
                                ParameterSignature = new[] { typeof(Type), typeof(string) }
                            })
                            .Store(delegateVariable)
                            .Load(delegateVariable)
                            .IsNotNull()
                            .Store(boolVariable)
                            .Load(boolVariable)
                            .MoveToWhenFalse(firstInstruction)
                            .NoOperation()
                            .Load(delegateVariable)
                            .LoadArray(parameters)
                            .InstanceCall(new Method
                            {
                                ParentType = typeof(Delegate),
                                MethodName = nameof(Delegate.DynamicInvoke),
                                ParameterSignature = new[] { typeof(object[]) }
                            });
        }

        private static MethodDefinition CreateGetInstanceMethod(ModuleDefinition md, MethodReference instanceMethodGetter, TypeDefinition type, bool hasImplementedIInstanceClass)
        {
            var getInstanceMethod = new MethodDefinition("GetInstanceMethod", MethodAttributes.Family,
                md.ImportReference(typeof(Delegate)));

            var methodName = new ParameterDefinition("methodName", ParameterAttributes.None,
                md.ImportReference(typeof(string)));
            getInstanceMethod.Parameters.Add(methodName);

            var boolVariable = new VariableDefinition(md.ImportReference(typeof(bool)));
            getInstanceMethod.Body.Variables.Add(boolVariable);

            var delegateVariable = new VariableDefinition(md.ImportReference(typeof(Delegate)));
            getInstanceMethod.Body.Variables.Add(delegateVariable);

            var composer1 = new InstructionComposer(md)
                .Load(delegateVariable)
                .Return();

            var composer2 = new InstructionComposer(md).LoadNull()
                .Store(delegateVariable)
                .MoveTo(composer1.Instructions.First());

            var composer = new InstructionComposer(md)
                .LoadArg_0()
                .InstanceCall(instanceMethodGetter)
                .Load(methodName)
                .InstanceCall(new Method
                {
                    ParentType = typeof(Dictionary<string, Delegate>),
                    MethodName = "ContainsKey",
                    ParameterSignature = new[] {typeof(Dictionary<string, Delegate>).GetGenericArguments()[0]}
                })
                .Store(boolVariable)
                .Load(boolVariable)
                .MoveToWhenFalse(composer2.Instructions.First())
                .LoadArg_0()
                .InstanceCall(instanceMethodGetter)
                .Load(methodName)
                .InstanceCall(new Method
                {
                    ParentType = typeof(Dictionary<string, Delegate>),
                    MethodName = "get_Item",
                    ParameterSignature = new[] {typeof(Dictionary<string, Delegate>).GetGenericArguments()[0]}
                })
                .Store(delegateVariable)
                .MoveTo(composer1.Instructions.First())
                .Append(composer2)
                .Append(composer1);

            var ilProcessor = getInstanceMethod.Body.GetILProcessor();

            foreach (var instruction in composer.Instructions) ilProcessor.Append(instruction);

            if (!type.IsAbstract & !hasImplementedIInstanceClass)
                type.Methods.Add(getInstanceMethod);

            return getInstanceMethod;
        }

        private static bool IsLoadInstruction(Instruction loadInstructionForReturn)
        {
            if (loadInstructionForReturn.OpCode == OpCodes.Ldloc_0)
                return true;
            if (loadInstructionForReturn.OpCode == OpCodes.Ldloc_1)
                return true;
            if (loadInstructionForReturn.OpCode == OpCodes.Ldloc_2)
                return true;
            if (loadInstructionForReturn.OpCode == OpCodes.Ldloc_3)
                return true;
            if (loadInstructionForReturn.OpCode == OpCodes.Ldloc_S)
                return true;
            if (loadInstructionForReturn.OpCode == OpCodes.Ldloc)
                return true;
            return false;
        }

        private static Instruction GetStoreInstruction(Instruction loadInstructionForReturn)
        {
            if (loadInstructionForReturn.OpCode == OpCodes.Ldloc_0)
                return Instruction.Create(OpCodes.Stloc_0);
            if (loadInstructionForReturn.OpCode == OpCodes.Ldloc_1)
                return Instruction.Create(OpCodes.Stloc_1);
            if (loadInstructionForReturn.OpCode == OpCodes.Ldloc_2)
                return Instruction.Create(OpCodes.Stloc_2);
            if (loadInstructionForReturn.OpCode == OpCodes.Ldloc_3)
                return Instruction.Create(OpCodes.Stloc_3);
            if (loadInstructionForReturn.OpCode == OpCodes.Ldloc_S)
                return Instruction.Create(OpCodes.Stloc_S, (VariableDefinition)loadInstructionForReturn.Operand);
            if (loadInstructionForReturn.OpCode == OpCodes.Ldloc)
                return Instruction.Create(OpCodes.Stloc, (VariableDefinition)loadInstructionForReturn.Operand);
            throw new Exception("Unable to get store locationn for opcode: " + loadInstructionForReturn.OpCode);
        }
    }
}