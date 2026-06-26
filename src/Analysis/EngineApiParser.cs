using System;
using System.Collections.Generic;
using System.Linq;
using Facepunch.AssemblySchema;
using SboxAstGraph.Model;

namespace SboxAstGraph.Analysis
{
    public class EngineApiParser
    {
        public Dictionary<string, ApiTypeNode> Parse(Schema schema)
        {
            var registry = new Dictionary<string, ApiTypeNode>(StringComparer.OrdinalIgnoreCase);

            foreach (var rawType in schema.Types.Where(t => t != null && !string.IsNullOrEmpty(t.FullName)))
            {
                string fullName = rawType.FullName!;

                // Якщо тип уже є в реєстрі (наприклад, partial клас або дублікат розширень)
                if (!registry.TryGetValue(fullName, out var typeNode))
                {
                    typeNode = new ApiTypeNode
                    {
                        Name = rawType.Name ?? string.Empty,
                        FullName = fullName,
                        Namespace = GetNamespace(fullName),
                        BaseType = rawType.BaseType,
                        IsInterface = rawType.IsInterface,
                        IsEnum = rawType.IsEnum,
                        IsValueType = rawType.IsEnum || rawType.BaseType == "System.ValueType",
                        Summary = rawType.Documentation?.Summary
                    };
                    registry[fullName] = typeNode;
                }

                // 1. Парсимо та додаємо поля
                if (rawType.Fields != null)
                {
                    foreach (var rawField in rawType.Fields.Where(f => f != null && !string.IsNullOrEmpty(f.Name)))
                    {
                        string docId = $"F:{fullName}.{rawField.Name}";
                        if (!typeNode.Fields.ContainsKey(docId))
                        {
                            typeNode.Fields[docId] = new ApiFieldNode
                            {
                                DocId = docId,
                                Name = rawField.Name!,
                                FieldType = rawField.FieldType ?? "object",
                                IsPublic = rawField.IsPublic,
                                IsStatic = rawField.IsStatic,
                                Summary = rawField.Documentation?.Summary
                            };
                        }
                    }
                }

                // 2. Парсимо та додаємо властивості
                if (rawType.Properties != null)
                {
                    foreach (var rawProp in rawType.Properties.Where(p => p != null && !string.IsNullOrEmpty(p.Name)))
                    {
                        string docId = $"P:{fullName}.{rawProp.Name}";
                        if (!typeNode.Properties.ContainsKey(docId))
                        {
                            typeNode.Properties[docId] = new ApiPropertyNode
                            {
                                DocId = docId,
                                Name = rawProp.Name!,
                                PropertyType = rawProp.PropertyType ?? "object",
                                IsPublic = rawProp.IsPublic,
                                IsStatic = rawProp.IsStatic,
                                Summary = rawProp.Documentation?.Summary
                            };
                        }
                    }
                }

                // 3. Парсимо та додаємо методи
                if (rawType.Methods != null)
                {
                    foreach (var rawMethod in rawType.Methods.Where(m => m != null && !string.IsNullOrEmpty(m.Name)))
                    {
                        // Самостійно будуємо унікальний .NET DocId на основі сигнатури (це надійно вирішує проблему перевантажень)
                        string paramSignature = string.Empty;
                        if (rawMethod.Parameters != null && rawMethod.Parameters.Count > 0)
                        {
                            paramSignature = string.Join(",", rawMethod.Parameters
                                .Where(p => p != null)
                                .Select(p => p.ParameterType ?? "object"));
                        }

                        string docId = $"M:{fullName}.{rawMethod.Name}({paramSignature})";

                        if (!typeNode.Methods.ContainsKey(docId))
                        {
                            var methodNode = new ApiMethodNode
                            {
                                DocId = docId,
                                Name = rawMethod.Name!,
                                ReturnType = rawMethod.ReturnType ?? "void",
                                IsPublic = rawMethod.IsPublic,
                                IsStatic = rawMethod.IsStatic,
                                IsExtension = rawMethod.IsExtension,
                                Summary = rawMethod.Documentation?.Summary
                            };

                            if (rawMethod.Parameters != null)
                            {
                                foreach (var rawParam in rawMethod.Parameters.Where(p => p != null && !string.IsNullOrEmpty(p.Name)))
                                {
                                    methodNode.Parameters.Add(new ApiParameterNode
                                    {
                                        Name = rawParam.Name!,
                                        ParameterType = rawParam.ParameterType ?? "object"
                                    });
                                }
                            }

                            typeNode.Methods[docId] = methodNode;
                        }
                    }
                }
            }

            return registry;
        }

        private static string GetNamespace(string fullName)
        {
            int lastDot = fullName.LastIndexOf('.');
            return lastDot == -1 ? "Sandbox" : fullName.Substring(0, lastDot);
        }
    }
}