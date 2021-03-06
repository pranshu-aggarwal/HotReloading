﻿using System;
using System.Collections.Generic;
using System.Linq;
using HotReloading.Core;
using HotReloading.Syntax;
using HotReloading.Syntax.Statements;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using StatementConverter.Extensions;

namespace StatementConverter.StatementInterpreter
{
    public class ObjectCreationStatementInterpreter : IStatementInterpreter
    {
        private readonly ObjectCreationExpressionSyntax objectCreationExpressionSyntax;
        private readonly StatementInterpreterHandler statementInterpreterHandler;
        private readonly SemanticModel semanticModel;

        public ObjectCreationStatementInterpreter(StatementInterpreterHandler statementInterpreterHandler,
            SemanticModel semanticModel,
            ObjectCreationExpressionSyntax objectCreationExpressionSyntax)
        {
            this.statementInterpreterHandler = statementInterpreterHandler;
            this.semanticModel = semanticModel;
            this.objectCreationExpressionSyntax = objectCreationExpressionSyntax;
        }

        public IStatementCSharpSyntax GetStatement()
        {
            var typeInfo = semanticModel.GetTypeInfo(objectCreationExpressionSyntax);
            if (typeInfo.Type.TypeKind == TypeKind.Delegate)
            {
                return CreateDelegateStatement(typeInfo.GetHrType());
            }

            return CreateObjectCreationStatement(typeInfo.GetHrType());
        }

        private IStatementCSharpSyntax CreateObjectCreationStatement(BaseHrType classType)
        {
            var objectCreationStatement = new ObjectCreationStatement
            {
                Type = classType
            };

            if (objectCreationExpressionSyntax.Initializer != null)
            {
                objectCreationStatement.Initializer = new List<IStatementCSharpSyntax>();

                foreach (var expression in objectCreationExpressionSyntax.Initializer.Expressions)
                {
                    var statement = statementInterpreterHandler.GetStatement(expression)
                    ;
                    objectCreationStatement.Initializer.Add(statement);
                }
            }

            var methodSymbolInfo = semanticModel.GetSymbolInfo(objectCreationExpressionSyntax);

            var arguments = new List<IStatementCSharpSyntax>();

            if (methodSymbolInfo.Symbol is IMethodSymbol methodSymbol)
            {
                for (int i = 0; i < methodSymbol.Parameters.Length; i++)
                {
                    var parameter = methodSymbol.Parameters[i];
                    if (!parameter.IsOptional)
                        arguments.Add(statementInterpreterHandler.GetStatement(objectCreationExpressionSyntax.ArgumentList.Arguments[i]));
                    else
                    {
                        var argumentSyntax = objectCreationExpressionSyntax.ArgumentList.Arguments.FirstOrDefault(x => x.NameColon != null && x.NameColon.Name.Identifier.ValueText == parameter.Name);
                        if (argumentSyntax == null)
                        {
                            if (objectCreationExpressionSyntax.ArgumentList.Arguments.Count <= i)
                            {
                                //use default value
                                arguments.Add(new ConstantStatement(parameter.ExplicitDefaultValue));
                            }
                            else
                                arguments.Add(statementInterpreterHandler.GetStatement(objectCreationExpressionSyntax.ArgumentList.Arguments[i]));
                        }
                        else
                            arguments.Add(statementInterpreterHandler.GetStatement(argumentSyntax));
                    }
                }
                objectCreationStatement.ParametersSignature = methodSymbol.Parameters.Select(x => x.Type.GetHrType()).ToArray();
            }
            else
            {
                throw new Exception("Cannot find constructor for type: " + classType);
            }

            objectCreationStatement.ArgumentList.AddRange(arguments);

            return objectCreationStatement;
        }

        private IStatementCSharpSyntax CreateDelegateStatement(BaseHrType classType)
        {
            if (objectCreationExpressionSyntax.ArgumentList.Arguments.Count != 1)
                throw new Exception("Cannot create instance of delegate type: " + classType);

            var objectCreationStatement = new DelegateObjectCreationStatement();

            objectCreationStatement.Method = statementInterpreterHandler.GetStatement(objectCreationExpressionSyntax.ArgumentList.Arguments[0]);

            return objectCreationStatement;
        }
    }
}