using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Facepunch.AssemblySchema;
using SboxAstGraph.Analysis;
using SboxAstGraph.Model;

namespace SboxAstGraph.Workspace
{
    public static class StubGenerator
    {
        // Реєстр для відстеження кількості дженерик-параметрів типів з метою авто-підстановки <object>
        private static Dictionary<string, int> _genericArities = new(StringComparer.OrdinalIgnoreCase);

        public static MetadataReference? GenerateMetadataReference(Schema schema)
        {
            Console.WriteLine("Генерація віртуальних заглушок для API S&box за новою архітектурою...");

            var codeBuilder = new StringBuilder();
            codeBuilder.AppendLine("#nullable disable"); // Вимикаємо перевірку nullable для стабільності
            codeBuilder.AppendLine("using System;");
            codeBuilder.AppendLine("using System.Collections;");
            codeBuilder.AppendLine("using System.Collections.Generic;");
            codeBuilder.AppendLine("using System.Threading.Tasks;");
            codeBuilder.AppendLine();

            // 1. Отримуємо унікальні типи без урахування регістру
            var uniqueTypes = schema.Types
                .Where(t => t != null && !string.IsNullOrEmpty(t.FullName))
                .GroupBy(t => t.FullName!, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            // Реєстр усіх повних назв класів для розумного розгортання вкладених типів
            var allClassFullNames = new HashSet<string>(uniqueTypes.Select(t => t.FullName!), StringComparer.OrdinalIgnoreCase);

            // Будуємо карту arity (дженериків) перед початком генерації з урахуванням інференсу та очищення тіків
            _genericArities.Clear();
            foreach (var type in uniqueTypes)
            {
                if (type.IsEnum) continue; // Енуми ніколи не мають дженериків!

                string rawName = type.Name ?? string.Empty;
                int tickIndex = rawName.IndexOf('`');
                int arity = 0;

                if (tickIndex != -1 && int.TryParse(rawName.Substring(tickIndex + 1), out int parsedArity) && parsedArity > 0)
                {
                    arity = parsedArity;
                }
                else
                {
                    var inferredParams = GetClassGenericParams(type, allClassFullNames);
                    arity = inferredParams.Count;
                }

                if (arity > 0)
                {
                    string strippedFullName = type.FullName!;
                    int tickPos = strippedFullName.IndexOf('`');
                    if (tickPos != -1)
                    {
                        strippedFullName = strippedFullName.Substring(0, tickPos);
                    }

                    var (cleanNs, cleanClass) = ResolveFlattenedType(strippedFullName, allClassFullNames);
                    string cleanFullName = string.IsNullOrEmpty(cleanNs) ? cleanClass : $"{cleanNs}.{cleanClass}";
                    _genericArities[cleanFullName] = arity;
                }
            }

            // 2. Виконуємо плоске розгортання для вкладених типів з урахуванням їхньої арності
            var flattenedTypes = uniqueTypes.Select(type =>
            {
                var (cleanNs, cleanClass) = ResolveFlattenedType(type.FullName!, allClassFullNames);

                string rawName = type.Name ?? string.Empty;
                int tickIndex = rawName.IndexOf('`');
                int arity = 0;
                if (tickIndex != -1 && int.TryParse(rawName.Substring(tickIndex + 1), out int parsedArity) && parsedArity > 0)
                {
                    arity = parsedArity;
                }
                else
                {
                    arity = GetClassGenericParams(type, allClassFullNames).Count;
                }

                return new { Type = type, Namespace = cleanNs, ClassName = cleanClass, Arity = arity };
            }).ToList();

            // 3. Групуємо розгорнуті типи за чистими просторами імен
            var typesByNamespace = flattenedTypes.GroupBy(x => string.IsNullOrEmpty(x.Namespace) ? "Sandbox" : x.Namespace).ToList();

            Action<StringBuilder> appendExternalStubs = (sb) =>
            {
                sb.AppendLine();
                sb.AppendLine("// --- ВІРТУАЛЬНІ МУЛЯЖІ ЗОВНІШНІХ БІБЛІОТЕК ДЛЯ СТАБІЛЬНОЇ КОМПІЛЯЦІЇ ---");
                sb.AppendLine("namespace Microsoft.AspNetCore.Components.Rendering { public class RenderTreeBuilder {} }");
                sb.AppendLine("namespace Microsoft.AspNetCore.Components { public class EventCallback {} public class RenderFragment {} public class RenderFragment<T> {} }");
                sb.AppendLine("namespace System.Net.Http { public class DelegatingHandler {} public class HttpContent {} public class HttpResponseMessage {} }");
                sb.AppendLine("namespace Microsoft.CodeAnalysis { public class SyntaxTree {} public class Diagnostic {} public class PortableExecutableReference {} public enum DiagnosticSeverity { Error } }");
                sb.AppendLine("namespace Microsoft.CodeAnalysis.Emit { public class EmitResult {} }");
                sb.AppendLine("namespace Microsoft.CodeAnalysis.CSharp { public class CSharpParseOptions {} }");
                sb.AppendLine("namespace System.Collections.Specialized { public enum NotifyCollectionChangedAction { Add } }");
                sb.AppendLine();
            };

            foreach (var nsGroup in typesByNamespace)
            {
                string ns = nsGroup.Key;
                codeBuilder.AppendLine($"namespace {ns}");
                codeBuilder.AppendLine("{");

                var uniqueClassesInNamespace = nsGroup
                    .GroupBy(x => new { x.ClassName, x.Arity })
                    .Select(g => g.First());

                foreach (var item in uniqueClassesInNamespace)
                {
                    GenerateTypeStub(codeBuilder, item.Type, item.ClassName, allClassFullNames);
                }

                codeBuilder.AppendLine("}");
                codeBuilder.AppendLine();
            }

            appendExternalStubs(codeBuilder);

            string generatedCode = codeBuilder.ToString();

            var syntaxTree = CSharpSyntaxTree.ParseText(generatedCode);
            var assemblyName = "SboxVirtualEngine";

            var systemFolder = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            var basicReferences = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(systemFolder, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(Path.Combine(systemFolder, "System.Private.Uri.dll")),
                MetadataReference.CreateFromFile(Path.Combine(systemFolder, "System.Collections.dll")),
                MetadataReference.CreateFromFile(Path.Combine(systemFolder, "System.Collections.Concurrent.dll")),
                MetadataReference.CreateFromFile(Path.Combine(systemFolder, "System.Collections.Immutable.dll")),
                MetadataReference.CreateFromFile(Path.Combine(systemFolder, "System.IO.Compression.dll")),
                MetadataReference.CreateFromFile(Path.Combine(systemFolder, "System.Linq.dll")),
                MetadataReference.CreateFromFile(Path.Combine(systemFolder, "System.Linq.Expressions.dll")),
                MetadataReference.CreateFromFile(Path.Combine(systemFolder, "System.Text.Json.dll")),
                MetadataReference.CreateFromFile(Path.Combine(systemFolder, "System.Reflection.dll")),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)
            };

            var compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { syntaxTree },
                basicReferences,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release)
            );

            using (var ms = new MemoryStream())
            {
                var result = compilation.Emit(ms);

                // --- САМОЗАГОЮВАЛЬНИЙ ПРОХІД (ПРОХІД 2) ---
                if (!result.Success)
                {
                    var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

                    if (errors.Any(e => e.Id == "CS0246" || e.Id == "CS0234" || e.Id == "CS0453"))
                    {
                        Console.WriteLine("[Аналіз] Запуск авто-загоювального компілятора (Self-Healing SDK)...");

                        var regexStruct = new System.Text.RegularExpressions.Regex(@"The type '([^']+)' must be a non-nullable value type");
                        foreach (var err in errors.Where(e => e.Id == "CS0453"))
                        {
                            var match = regexStruct.Match(err.GetMessage());
                            if (match.Success)
                            {
                                string structName = match.Groups[1].Value.Trim();
                                int lastDot = structName.LastIndexOf('.');
                                if (lastDot != -1) structName = structName.Substring(lastDot + 1);
                                ForcedStructs.Add(structName);
                            }
                        }

                        var healingCodeBuilder = new StringBuilder();
                        healingCodeBuilder.AppendLine("#nullable disable");
                        healingCodeBuilder.AppendLine("using System; using System.Collections; using System.Collections.Generic; using System.Threading.Tasks;");

                        foreach (var nsGroup in typesByNamespace)
                        {
                            healingCodeBuilder.AppendLine($"namespace {nsGroup.Key} {{");
                            var uniqueClassesInNamespace = nsGroup.GroupBy(x => new { x.ClassName, x.Arity }).Select(g => g.First());
                            foreach (var item in uniqueClassesInNamespace)
                            {
                                GenerateTypeStub(healingCodeBuilder, item.Type, item.ClassName, allClassFullNames);
                            }
                            healingCodeBuilder.AppendLine("}");
                        }

                        appendExternalStubs(healingCodeBuilder); // Додаємо віртуальні муляжі у 2-й прохід!

                        string dynamicStubs = GenerateMissingStubsOnTheFly(errors, allClassFullNames);
                        generatedCode = healingCodeBuilder.ToString() + dynamicStubs;

                        var healingSyntaxTree = CSharpSyntaxTree.ParseText(generatedCode);
                        var healingCompilation = CSharpCompilation.Create(
                            assemblyName,
                            new[] { healingSyntaxTree },
                            basicReferences,
                            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release)
                        );

                        ms.SetLength(0);
                        result = healingCompilation.Emit(ms);
                    }
                }

                if (!result.Success)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[Попередження] Не вдалося скомпілювати віртуальну збірку заглушок API S&box.");

                    try
                    {
                        string debugFilePath = Path.Combine(Directory.GetCurrentDirectory(), "temp_api_stub.cs");
                        File.WriteAllText(debugFilePath, generatedCode);
                        Console.WriteLine($"  -> Згенерований код з помилкою збережено у: {debugFilePath}");
                    }
                    catch { }

                    string[] codeLines = generatedCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                    int totalErrors = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
                    var aggregatedErrors = result.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .GroupBy(d => d.GetMessage())
                        .Select(group => new
                        {
                            Message = group.Key,
                            Count = group.Count(),
                            FirstOccur = group.First()
                        })
                        .Take(100)
                        .ToList();

                    Console.WriteLine($"\n--- АГРЕГОВАНИЙ АНАЛІЗ КРИТИЧНИХ ПОМИЛОК (Всього помилок: {totalErrors}, унікальних типів: {aggregatedErrors.Count}) ---");

                    foreach (var err in aggregatedErrors)
                    {
                        var diagnostic = err.FirstOccur;
                        var lineSpan = diagnostic.Location.GetLineSpan();
                        string countSuffix = err.Count > 1 ? $" [випадків: {err.Count}]" : "";

                        if (lineSpan.IsValid)
                        {
                            int zeroBasedLine = lineSpan.StartLinePosition.Line;
                            int oneBasedLine = zeroBasedLine + 1;

                            string offendingLine = (zeroBasedLine >= 0 && zeroBasedLine < codeLines.Length)
                                ? codeLines[zeroBasedLine].Trim()
                                : "[код недоступний]";

                            Console.WriteLine($"  -> Рядок {oneBasedLine}{countSuffix}: {err.Message}");
                            Console.WriteLine($"     Проблемний код:  {offendingLine}\n");
                        }
                        else
                        {
                            Console.WriteLine($"  -> {err.Message}{countSuffix}\n");
                        }
                    }
                    Console.ResetColor();
                    return null;
                }

                ms.Seek(0, SeekOrigin.Begin);
                Console.WriteLine("[ОК] Віртуальну збірку заглушок успішно створено.");
                return MetadataReference.CreateFromStream(ms);
            }
        }

        public static (string CleanNamespace, string CleanClassName) ResolveFlattenedType(string fullName, HashSet<string> allClassFullNames)
        {
            string ns = GetNamespace(fullName);
            string shortName = GetShortName(fullName);

            if (allClassFullNames.Contains(ns))
            {
                var parent = ResolveFlattenedType(ns, allClassFullNames);
                string cleanClassName = SanitizeName($"{parent.CleanClassName}_{shortName}");
                return (parent.CleanNamespace, cleanClassName);
            }

            return (SanitizeNamespace(ns), SanitizeName(shortName));
        }

        private static void GenerateTypeStub(StringBuilder sb, Schema.Type type, string cleanClassName, HashSet<string> allClassFullNames)
        {
            string rawName = type.Name ?? string.Empty;
            int tickIndex = rawName.IndexOf('`');
            string genericParamsDecl = string.Empty;

            var classParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (tickIndex != -1 && int.TryParse(rawName.Substring(tickIndex + 1), out int arity) && arity > 0)
            {
                if (arity == 1)
                {
                    genericParamsDecl = "<GP_T>";
                    classParams.Add("GP_T");
                }
                else
                {
                    var genericList = Enumerable.Range(1, arity).Select(i => $"GP_T{i}");
                    genericParamsDecl = $"<{string.Join(", ", genericList)}>";
                    foreach (var p in genericList) classParams.Add(p);
                }
            }

            // Автовизначення дженериків класу за членами з урахуванням префіксу безпеки
            if (string.IsNullOrEmpty(genericParamsDecl))
            {
                var inferredParams = GetClassGenericParams(type, allClassFullNames);
                if (inferredParams.Count > 0)
                {
                    genericParamsDecl = $"<{string.Join(", ", inferredParams.Select(p => "GP_" + p))}>";
                    foreach (var p in inferredParams) classParams.Add("GP_" + p);
                }
            }

            // Визначаємо вид типу
            if (type.IsEnum)
            {
                sb.AppendLine($"    public enum {cleanClassName}");
                sb.AppendLine("    {");
                if (type.Fields != null)
                {
                    foreach (var field in type.Fields.Where(f => f != null && f.Name != "value__"))
                    {
                        sb.AppendLine($"        {SanitizeMemberName(field.Name)},");
                    }
                }
                sb.AppendLine("    }");
            }
            else if (type.IsInterface)
            {
                sb.AppendLine($"    public interface {cleanClassName}{genericParamsDecl}");
                sb.AppendLine("    {");
                if (type.Methods != null)
                {
                    var declaredInterfaceSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var method in type.Methods.Where(m => m != null && m.IsPublic && m.Name != ".ctor"))
                    {
                        string rawMethodName = method.Name ?? string.Empty;
                        int mTickIndex = rawMethodName.IndexOf('`');
                        string mName = SanitizeMemberName(mTickIndex != -1 ? rawMethodName.Substring(0, mTickIndex) : rawMethodName);
                        string mGenericParamsDecl = string.Empty;

                        var inferredMethodParams = GetMethodGenericParams(method, allClassFullNames, genericParamsDecl);
                        if (inferredMethodParams.Count > 0)
                        {
                            mGenericParamsDecl = $"<{string.Join(", ", inferredMethodParams)}>";
                        }

                        string paramsStr = BuildParamsString(method.Parameters, allClassFullNames);
                        string retType = ResolveTypeForStub(method.ReturnType, allClassFullNames);

                        string signatureKey = $"{mName}{mGenericParamsDecl}_{paramsStr}";

                        if (declaredInterfaceSignatures.Add(signatureKey))
                        {
                            sb.AppendLine($"        {retType} {mName}{mGenericParamsDecl}({paramsStr});");
                        }
                    }
                }
                sb.AppendLine("    }");
            }
            else if (!string.IsNullOrEmpty(type.BaseType) &&
                     (type.BaseType.Contains("System.MulticastDelegate") || type.BaseType.Contains("MulticastDelegate")))
            {
                var invokeMethod = type.Methods?.FirstOrDefault(m => m != null && m.Name == "Invoke");
                string retType = "void";
                string paramsStr = "";

                if (invokeMethod != null)
                {
                    retType = ResolveTypeForStub(invokeMethod.ReturnType, allClassFullNames);
                    paramsStr = BuildParamsString(invokeMethod.Parameters, allClassFullNames);
                }

                sb.AppendLine($"    public delegate {retType} {cleanClassName}{genericParamsDecl}({paramsStr});");
            }
            else // Class або Struct
            {
                bool isStruct = ForcedStructs.Contains(cleanClassName) ||
                                (!string.IsNullOrEmpty(type.BaseType) &&
                                 (type.BaseType.Contains("System.ValueType") || type.BaseType.Contains("ValueType")));

                string typeKeyword = isStruct ? "struct" : "class";
                string baseClassDecl = "";

                if (!isStruct && !string.IsNullOrEmpty(type.BaseType) && type.BaseType != "System.Object" && type.BaseType != "System.ValueType" && type.BaseType != "Object")
                {
                    string baseName = FormatTypeSignatureForStub(TypeResolver.Parse(type.BaseType), allClassFullNames);

                    // Якщо базовий клас є локальним не-дженериком, але успадковується з параметрами (наприклад, BasePostProcess<GP_T>),
                    // повністю зрізаємо параметри. Системні типи (List, JsonConverter) не чіпаємо!
                    int angleIndex = baseName.IndexOf('<');
                    if (angleIndex != -1)
                    {
                        string baseLookup = baseName.Substring(0, angleIndex).Replace("global::", "").Trim();
                        if (allClassFullNames.Contains(baseLookup))
                        {
                            if (!_genericArities.TryGetValue(baseLookup, out int bArity) || bArity == 0)
                            {
                                baseName = baseName.Substring(0, angleIndex);
                            }
                        }
                    }

                    // Перевіряємо кожен можливий параметр: якщо його немає у поточному класі, замінюємо на object у базовому класі
                    string[] possibleParams = { "GP_T", "GP_TKey", "GP_TValue", "GP_TSelf", "GP_TResult", "GP_Type" };
                    foreach (var param in possibleParams)
                    {
                        if (!classParams.Contains(param)) // Точний пошук без колізій підрядків (GP_T vs GP_Type)
                        {
                            baseName = baseName
                                .Replace($"<{param}>", "<object>")
                                .Replace($", {param}>", ", object>")
                                .Replace($"<{param},", "<object,")
                                .Replace($", {param},", ", object,");
                        }
                    }
                    baseClassDecl = $" : {baseName}";
                }

                sb.AppendLine($"    public {typeKeyword} {cleanClassName}{genericParamsDecl}{baseClassDecl}");
                sb.AppendLine("    {");

                // Поля класу (для структур повертаємо default без створення backing-поля)
                if (type.Fields != null)
                {
                    foreach (var field in type.Fields.Where(f => f != null && f.IsPublic))
                    {
                        string fType = ResolveTypeForStub(field.FieldType, allClassFullNames);
                        string fName = SanitizeMemberName(field.Name);
                        string @static = field.IsStatic ? "static " : "";

                        if (isStruct && string.IsNullOrEmpty(@static))
                        {
                            sb.AppendLine($"        public {fType} {fName} => default;");
                        }
                        else
                        {
                            sb.AppendLine($"        public {@static}{fType} {fName} {{ get; set; }}");
                        }
                    }
                }

                // Властивості класу (для структур повертаємо default для уникнення циклів struct layout)
                if (type.Properties != null)
                {
                    foreach (var prop in type.Properties.Where(p => p != null && p.IsPublic))
                    {
                        string pType = ResolveTypeForStub(prop.PropertyType, allClassFullNames);
                        string pName = SanitizeMemberName(prop.Name);
                        string @static = prop.IsStatic ? "static " : "";

                        if (isStruct && string.IsNullOrEmpty(@static))
                        {
                            sb.AppendLine($"        public {pType} {pName} => default;");
                        }
                        else
                        {
                            sb.AppendLine($"        public {@static}{pType} {pName} {{ get; set; }}");
                        }
                    }
                }

                // Методи та конструктори
                if (type.Methods != null)
                {
                    var declaredSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    bool isJsonConverter = baseClassDecl.Contains("JsonConverter<") || baseClassDecl.Contains("JsonConverter");

                    foreach (var method in type.Methods.Where(m => m != null && m.IsPublic))
                    {
                        // Скидаємо оригінальні Read/Write для JsonConverter, бо ми згенеруємо їх самі без помилок сигнатур
                        if (isJsonConverter && (method.Name == "Read" || method.Name == "Write"))
                            continue;

                        bool isConstructor = method.Name == ".ctor" || method.Name == "ctor";

                        string rawMethodName = method.Name ?? string.Empty;
                        int mTickIndex = rawMethodName.IndexOf('`');
                        string mName = isConstructor ? cleanClassName : SanitizeMemberName(mTickIndex != -1 ? rawMethodName.Substring(0, mTickIndex) : rawMethodName);
                        string mGenericParamsDecl = string.Empty;

                        if (!isConstructor)
                        {
                            var inferredMethodParams = GetMethodGenericParams(method, allClassFullNames, genericParamsDecl);
                            if (inferredMethodParams.Count > 0)
                            {
                                mGenericParamsDecl = $"<{string.Join(", ", inferredMethodParams.Select(p => "GP_" + p))}>";
                            }
                        }

                        string paramsStr = BuildParamsString(method.Parameters, allClassFullNames);
                        string retType = ResolveTypeForStub(method.ReturnType, allClassFullNames);

                        string @static = (!isConstructor && method.IsStatic) ? "static " : "";

                        // Автовизначення дженериків методу
                        bool parentClassHasT = genericParamsDecl.Contains("<T>");
                        if (!isConstructor && !parentClassHasT && string.IsNullOrEmpty(mGenericParamsDecl) && UsesGenericParams(paramsStr, retType))
                        {
                            mGenericParamsDecl = "<T>";
                        }

                        // Будуємо ключ порівняння строго за типами параметрів для точного відсікання дублікатів C#
                        string paramTypesOnly = method.Parameters != null
                            ? string.Join("_", method.Parameters.Where(p => p != null).Select(p => ResolveTypeForStub(p.ParameterType, allClassFullNames)))
                            : "";
                        string signatureKey = $"{mName}{mGenericParamsDecl}_{paramTypesOnly}";

                        if (declaredSignatures.Add(signatureKey))
                        {
                            if (isConstructor)
                            {
                                sb.AppendLine($"        public {cleanClassName}({paramsStr}) {{}}");
                            }
                            else
                            {
                                string body = retType == "void" ? "{}" : "{ return default; }";
                                sb.AppendLine($"        public {@static}{retType} {mName}{mGenericParamsDecl}({paramsStr}) {body}");
                            }
                        }
                    }
                    // Авто-імплантація для спадкоємців JsonConverter для запобігання помилкам абстракції
                    if (baseClassDecl.Contains("JsonConverter<") || baseClassDecl.Contains("JsonConverter"))
                    {
                        string convertedType = "object";
                        int start = baseClassDecl.IndexOf('<');
                        int end = baseClassDecl.LastIndexOf('>');
                        if (start != -1 && end != -1 && end > start)
                        {
                            convertedType = baseClassDecl.Substring(start + 1, end - start - 1);
                        }

                        bool hasRead = declaredSignatures.Any(s => s.StartsWith("Read_", StringComparison.OrdinalIgnoreCase));
                        bool hasWrite = declaredSignatures.Any(s => s.StartsWith("Write_", StringComparison.OrdinalIgnoreCase));

                        sb.AppendLine();
                        if (!hasRead)
                        {
                            sb.AppendLine($"        public override {convertedType} Read(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options) {{ return default; }}");
                        }
                        if (!hasWrite)
                        {
                            sb.AppendLine($"        public override void Write(global::System.Text.Json.Utf8JsonWriter writer, {convertedType} value, global::System.Text.Json.JsonSerializerOptions options) {{}}");
                        }
                    }
                }

                sb.AppendLine("    }");
            }
            sb.AppendLine();
        }

        private static string BuildParamsString(IList<Schema.Method.Parameter>? parameters, HashSet<string> allClassFullNames)
        {
            if (parameters == null) return string.Empty;

            var paramList = new List<string>();
            foreach (var param in parameters.Where(p => p != null))
            {
                string pTypeName = param.ParameterType ?? "object";
                bool isRef = pTypeName.EndsWith("@");
                string resolvedType = ResolveTypeForStub(pTypeName, allClassFullNames);
                string pName = SanitizeMemberName(param.Name);
                string modifier = isRef ? "ref " : "";
                paramList.Add($"{modifier}{resolvedType} {pName}");
            }

            return string.Join(", ", paramList);
        }

        private static string ResolveTypeForStub(string? fullName, HashSet<string> allClassFullNames)
        {
            if (string.IsNullOrEmpty(fullName)) return "void";
            var signature = TypeResolver.Parse(fullName);
            return FormatTypeSignatureForStub(signature, allClassFullNames);
        }

        private static string FormatTypeSignatureForStub(TypeSignature signature, HashSet<string> allClassFullNames)
        {
            if (signature == null) return "object";

            // Якщо це Span або ReadOnlySpan, конвертуємо в масив для зняття обмежень ref struct
            if (signature.FullName.Contains("System.ReadOnlySpan") || signature.FullName.Contains("System.Span"))
            {
                string innerType = signature.GenericArguments.Count > 0
                    ? FormatTypeSignatureForStub(signature.GenericArguments[0], allClassFullNames)
                    : "byte";
                return $"{innerType}[]";
            }

            string name = signature.FullName switch
            {
                "System.Void" => "void",
                "System.Int32" => "int",
                "System.Int64" => "long",
                "System.Single" => "float",
                "System.Double" => "double",
                "System.Boolean" => "bool",
                "System.String" => "string",
                "System.Char" => "char",
                "System.Object" => "object",
                _ => GetFullyQualifiedName(signature.FullName, allClassFullNames)
            };

            if (signature.GenericArguments.Count > 0)
            {
                string lookupName = name.StartsWith("global::") ? name.Substring(8) : name;
                int requiredArityVal = 0;
                _genericArities.TryGetValue(lookupName, out requiredArityVal);

                // Якщо локальний тип є не-дженериком за реєстром, ми повністю ігноруємо передані дженерик-параметри
                if (allClassFullNames.Contains(lookupName) && requiredArityVal == 0)
                {
                    // Не додаємо кутові дужки дженериків взагалі
                }
                else
                {
                    string args = string.Join(", ", signature.GenericArguments.Select(arg => FormatTypeSignatureForStub(arg, allClassFullNames)));

                    // Якщо передано менше дженериків, ніж вимагає сигнатура типу, автоматично дописуємо <..., object>
                    if (requiredArityVal > signature.GenericArguments.Count)
                    {
                        var extraArgs = Enumerable.Repeat("object", requiredArityVal - signature.GenericArguments.Count);
                        args += ", " + string.Join(", ", extraArgs);
                    }

                    name += $"<{args}>";
                }
            }
            // АВТО-ПІДСТАНОВКА ДЖЕНЕРИКІВ (якщо тип є дженериком, але викликається без параметрів)
            else
            {
                string lookupName = name.StartsWith("global::") ? name.Substring(8) : name;

                int underscoreIndex = lookupName.LastIndexOf('_');
                if (underscoreIndex != -1 && underscoreIndex < lookupName.Length - 1 && char.IsDigit(lookupName[underscoreIndex + 1]))
                {
                    lookupName = lookupName.Substring(0, underscoreIndex);
                }

                if (_genericArities.TryGetValue(lookupName, out int arity) && arity > 0)
                {
                    var fallbackArgs = Enumerable.Repeat("object", arity);
                    name += $"<{string.Join(", ", fallbackArgs)}>";
                }
            }

            if (signature.IsArray) name += "[]";
            return name;
        }

        private static string GetFullyQualifiedName(string fullName, HashSet<string> allClassFullNames)
        {
            if (!fullName.Contains(".") && !allClassFullNames.Contains(fullName))
            {
                return "GP_" + SanitizeName(fullName);
            }

            var (cleanNs, cleanClass) = ResolveFlattenedType(fullName, allClassFullNames);

            if (string.IsNullOrEmpty(cleanNs))
            {
                return $"global::{cleanClass}";
            }

            return $"global::{cleanNs}.{cleanClass}";
        }

        private static string GetNamespace(string fullName)
        {
            int lastDot = fullName.LastIndexOf('.');
            return lastDot == -1 ? "" : fullName.Substring(0, lastDot);
        }

        private static string GetShortName(string fullName)
        {
            int lastDot = fullName.LastIndexOf('.');
            return lastDot == -1 ? fullName : fullName.Substring(lastDot + 1);
        }

        public static string SanitizeNamespace(string ns)
        {
            if (string.IsNullOrEmpty(ns)) return "Sandbox";
            var parts = ns.Split('.');
            var cleanParts = parts.Select(part => SanitizeName(part));
            return string.Join(".", cleanParts);
        }

        public static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            return name
                .Replace("+", "_")
                .Replace("/", "_")
                .Replace("<", "_")
                .Replace(">", "_")
                .Replace(",", "_")
                .Replace("`", "_")
                .Replace("$", "_")
                .Replace("|", "_")
                .Replace(" ", "");
        }

        public static string SanitizeMemberName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "TempName";

            string clean = name
                .Replace(".", "_")
                .Replace("+", "_")
                .Replace("<", "_")
                .Replace(">", "_")
                .Replace("$", "_")
                .Replace("|", "_")
                .Replace(" ", "");

            if (clean.Length > 0 && char.IsDigit(clean[0]))
            {
                clean = "_" + clean;
            }

            string[] keywords = {
                "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
                "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
                "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
                "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
                "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
                "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
                "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this",
                "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
                "using", "virtual", "void", "volatile", "while"
                };

            if (keywords.Contains(clean))
            {
                return "@" + clean;
            }

            return clean;
        }

        private static bool IsGenericParam(string typeName, HashSet<string> allClassFullNames)
        {
            if (string.IsNullOrEmpty(typeName)) return false;

            string cleanName = typeName;
            int dot = typeName.LastIndexOf('.');
            if (dot != -1) cleanName = typeName.Substring(dot + 1);

            if (allClassFullNames.Contains(cleanName) || allClassFullNames.Contains(typeName))
                return false;

            // Дженериками вважаємо поодинокі літери (T, U), слова на 'T' (TKey, TargetType) та слова, що закінчуються на 'Type'
            return (cleanName.Length == 1 && char.IsUpper(cleanName[0])) ||
                   cleanName.StartsWith("T") ||
                   cleanName.EndsWith("Type");
        }

        private static bool UsesGenericParams(string paramsStr, string retType)
        {
            return paramsStr.Contains("<T>") || paramsStr.Contains("<TKey>") || paramsStr.Contains("<TValue>") ||
                   paramsStr.Contains("<TSource>") || paramsStr.Contains("<TResult>") ||
                   paramsStr.Contains("<U>") || paramsStr.Contains("U ") ||
                   paramsStr.Contains(", TKey>") || paramsStr.Contains(", TValue>") ||
                   paramsStr.Contains("TKey ") || paramsStr.Contains("TValue ") || paramsStr.Contains("T ") ||
                   retType.Contains("<T>") || retType.Contains("<TKey>") || retType.Contains("<TValue>") ||
                   retType == "T" || retType == "TKey" || retType == "TValue" || retType == "U" || retType.Contains("<U>");
        }

        private static List<string> GetClassGenericParams(Schema.Type type, HashSet<string> allClassFullNames)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (type.Properties != null)
            {
                foreach (var prop in type.Properties.Where(p => p != null))
                {
                    var signature = TypeResolver.Parse(prop.PropertyType);
                    if (IsGenericParam(signature.CleanName, allClassFullNames))
                        result.Add(signature.CleanName);
                    foreach (var arg in signature.GenericArguments)
                    {
                        if (IsGenericParam(arg.CleanName, allClassFullNames))
                            result.Add(arg.CleanName);
                    }
                }
            }

            if (type.Fields != null)
            {
                foreach (var field in type.Fields.Where(f => f != null))
                {
                    var signature = TypeResolver.Parse(field.FieldType);
                    if (IsGenericParam(signature.CleanName, allClassFullNames))
                        result.Add(signature.CleanName);
                    foreach (var arg in signature.GenericArguments)
                    {
                        if (IsGenericParam(arg.CleanName, allClassFullNames))
                            result.Add(arg.CleanName);
                    }
                }
            }

            if (type.Methods != null)
            {
                foreach (var method in type.Methods.Where(m => m != null))
                {
                    var retSig = TypeResolver.Parse(method.ReturnType);
                    if (IsGenericParam(retSig.CleanName, allClassFullNames))
                        result.Add(retSig.CleanName);
                    foreach (var arg in retSig.GenericArguments)
                    {
                        if (IsGenericParam(arg.CleanName, allClassFullNames))
                            result.Add(arg.CleanName);
                    }

                    if (method.Parameters != null)
                    {
                        foreach (var param in method.Parameters.Where(p => p != null))
                        {
                            var paramSig = TypeResolver.Parse(param.ParameterType);
                            if (IsGenericParam(paramSig.CleanName, allClassFullNames))
                                result.Add(paramSig.CleanName);
                            foreach (var arg in paramSig.GenericArguments)
                            {
                                if (IsGenericParam(arg.CleanName, allClassFullNames))
                                    result.Add(arg.CleanName);
                            }
                        }
                    }
                }
            }

            return result.OrderBy(x => x).ToList();
        }

        private static List<string> GetMethodGenericParams(Schema.Method method, HashSet<string> allClassFullNames, string classGenericParams)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var retSig = TypeResolver.Parse(method.ReturnType);
            if (IsGenericParam(retSig.CleanName, allClassFullNames))
                result.Add(retSig.CleanName);
            foreach (var arg in retSig.GenericArguments)
            {
                if (IsGenericParam(arg.CleanName, allClassFullNames))
                    result.Add(arg.CleanName);
            }

            if (method.Parameters != null)
            {
                foreach (var param in method.Parameters.Where(p => p != null))
                {
                    var paramSig = TypeResolver.Parse(param.ParameterType);
                    if (IsGenericParam(paramSig.CleanName, allClassFullNames))
                        result.Add(paramSig.CleanName);
                    foreach (var arg in paramSig.GenericArguments)
                    {
                        if (IsGenericParam(arg.CleanName, allClassFullNames))
                            result.Add(arg.CleanName);
                    }
                }
            }

            if (!string.IsNullOrEmpty(classGenericParams))
            {
                string cleanClassParams = classGenericParams.Replace("<", "").Replace(">", "").Replace(" ", "");
                var parentParams = cleanClassParams.Split(',');
                foreach (var parentParam in parentParams)
                {
                    result.Remove(parentParam);
                }
            }

            return result.OrderBy(x => x).ToList();
        }

        public static readonly HashSet<string> ForcedStructs = new(StringComparer.OrdinalIgnoreCase)
        {
            "Color", "Color32", "Vector2", "Vector3", "Vector4", "Angles", "Rotation", "SceneTraceResult",
            "Ray", "BBox", "Plane", "Matrix", "CreateSubGraphResult", "Transform", "Length", "PanelTransform",
            "SceneReferenceNode", "PhysicsBodyBuilder_HullSimplify", "Component_IPressable_Tooltip",
            "Json_ObjectIdentifier", "EmbeddedResource", "CloneConfig", "NavMeshAgent_LinkTraversalData",
            "Terrain_TerrainMaterialInfo", "Connection_Filter", "MovieTimeRange", "MovieTime",
            "MountResourceInfo", "GradientFogSetup", "SteamId",
            "SoundFile_PcmOptions", "SoundFile_LoadOptions", "PcmOptions", "LoadOptions"
        };

        /// <summary>
        /// Автоматично аналізує помилки компіляції Roslyn і генерує динамічні заглушки для відсутніх типів.
        /// </summary>
        private static string GenerateMissingStubsOnTheFly(IEnumerable<Diagnostic> diagnostics, HashSet<string> allClassFullNames)
        {
            var sb = new StringBuilder();
            var registeredStubs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Регулярні вирази для розбору повідомлень компілятора C#
            var regexInNamespace = new System.Text.RegularExpressions.Regex(
                @"The type or namespace name '([^'<]+)[^']*' does not exist in the namespace '([^']+)'",
                System.Text.RegularExpressions.RegexOptions.Compiled);

            var regexNotFound = new System.Text.RegularExpressions.Regex(
                @"The type or namespace name '([^'<]+)[^']*' could not be found",
                System.Text.RegularExpressions.RegexOptions.Compiled);

            sb.AppendLine();
            sb.AppendLine("// --- АВТОМАТИЧНО ЗГЕНЕРОВАНІ ДИНАМІЧНІ ЗАГЛУШКИ ДЛЯ ЗОВНІШНІХ БІБЛІОТЕК ---");

            foreach (var diag in diagnostics.Where(d => d.Id == "CS0246" || d.Id == "CS0234"))
            {
                string msg = diag.GetMessage();
                string typeName = "";
                string nsName = "Sandbox";

                var match = regexInNamespace.Match(msg);
                if (match.Success)
                {
                    typeName = match.Groups[1].Value.Trim();
                    nsName = match.Groups[2].Value.Trim();
                }
                else
                {
                    match = regexNotFound.Match(msg);
                    if (match.Success)
                    {
                        typeName = match.Groups[1].Value.Trim();
                    }
                }

                // Ігноруємо базові системні літери-дженерики
                if (string.IsNullOrEmpty(typeName) || typeName == "T" || typeName == "TKey" || typeName == "TValue" || typeName == "U" || typeName == "TSelf" || typeName == "TValue")
                    continue;

                // Запобігаємо конфлікту класів та просторів імен для вкладених речей типу CircularBuffer.Enumerator
                if (typeName == "Enumerator" && nsName.EndsWith("CircularBuffer"))
                {
                    nsName = nsName.Substring(0, nsName.Length - 15);
                    typeName = "CircularBuffer";
                }

                string uniqueKey = $"{nsName}.{typeName}";

                // Ніколи не створюємо дублікат заглушки, якщо цей клас вже згенерований рушієм!
                if (allClassFullNames.Contains(uniqueKey)) continue;

                if (registeredStubs.Add(uniqueKey))
                {
                    // Генеруємо універсальний набір перевантажень типу з вбудованими заглушками під IEnumerator
                    sb.AppendLine($"namespace {nsName}");
                    sb.AppendLine("{");
                    sb.AppendLine($"    public class {typeName}");
                    sb.AppendLine("    {");
                    sb.AppendLine("        public class Enumerator {}");
                    sb.AppendLine("        public class Enumerator<T> {}");
                    sb.AppendLine("    }");
                    sb.AppendLine($"    public class {typeName}<T>");
                    sb.AppendLine("    {");
                    sb.AppendLine("        public class Enumerator {}");
                    sb.AppendLine("        public class Enumerator<T1> {}");
                    sb.AppendLine("    }");
                    sb.AppendLine($"    public class {typeName}<T1, T2>");
                    sb.AppendLine("    {");
                    sb.AppendLine("        public class Enumerator {}");
                    sb.AppendLine("        public class Enumerator<T> {}");
                    sb.AppendLine("    }");
                    sb.AppendLine("}");
                }
            }

            return sb.ToString();
        }
    }
}

