﻿using System.Collections.Generic;
using System.Linq;
using HotReloading.Core.Statements;
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

        public Statement GetStatement()
        {
            var typeInfo = semanticModel.GetTypeInfo(objectCreationExpressionSyntax);

            var objectCreationStatement = new ObjectCreationStatement
            {
                Type = typeInfo.GetClassType()
            };

            if (objectCreationExpressionSyntax.Initializer != null)
            {
                objectCreationStatement.Initializer = new List<Statement>();

                foreach(var expression in objectCreationExpressionSyntax.Initializer.Expressions)
                {
                    var statement = statementInterpreterHandler.GetStatement(expression)
                    ;
                    objectCreationStatement.Initializer.Add(statement);
                }
            }

            var methodSymbolInfo = semanticModel.GetSymbolInfo(objectCreationExpressionSyntax);

            var arguments = new List<Statement>();

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
                objectCreationStatement.ParametersSignature = methodSymbol.Parameters.Select(x => x.Type.GetClassType()).ToArray();
            }

            objectCreationStatement.ArgumentList.AddRange(arguments);

            return objectCreationStatement;
        }
    }
}