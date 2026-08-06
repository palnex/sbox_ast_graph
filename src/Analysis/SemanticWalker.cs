using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SboxAstGraph.Filtering;
using SboxAstGraph.Model;

namespace SboxAstGraph.Analysis
{
    public class SemanticWalker : CSharpSyntaxWalker
    {
        private readonly SemanticModel _semanticModel;
        private readonly TypeFilter _filter;
        private readonly CodeGraph _graph;
        private readonly string _filePath;
        private readonly HashSet<string> _knownClasses;

        private readonly Stack<string> _classContextStack = new();

        public SemanticWalker(SemanticModel semanticModel, TypeFilter filter, CodeGraph graph, string filePath, HashSet<string> knownClasses)
        {
            _semanticModel = semanticModel;
            _filter = filter;
            _graph = graph;
            _filePath = filePath;
            _knownClasses = knownClasses;
        }

        private string? CurrentClass => _classContextStack.Count > 0 ? _classContextStack.Peek() : null;

        // 1. Оголошення класу та наслідування
        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var classSymbol = _semanticModel.GetDeclaredSymbol(node);

            if (classSymbol != null)
            {
                string className = classSymbol.Name;
                string ns = classSymbol.ContainingNamespace?.ToDisplayString() ?? "SboxGeneratedRazorSpace";

                _graph.AddNode(className, _filePath, ns);

                var baseType = classSymbol.BaseType;
                if (baseType != null && baseType.SpecialType == SpecialType.None && baseType.ToDisplayString() != "object")
                {
                    CheckAndAddDependency(className, baseType, "Inherits", "Base Class");
                }

                _classContextStack.Push(className);
                base.VisitClassDeclaration(node);
                _classContextStack.Pop();
            }
            else
            {
                base.VisitClassDeclaration(node);
            }
        }

        // 1.1 Оголошення структур (struct)
        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
            var structSymbol = _semanticModel.GetDeclaredSymbol(node);
            if (structSymbol != null)
            {
                string structName = structSymbol.Name;
                string ns = structSymbol.ContainingNamespace?.ToDisplayString() ?? "SboxGeneratedRazorSpace";

                _graph.AddNode(structName, _filePath, ns);
                _classContextStack.Push(structName);

                base.VisitStructDeclaration(node);

                _classContextStack.Pop();
            }
            else
            {
                base.VisitStructDeclaration(node);
            }
        }

        // 2. Створення нових об'єктів (new Mesh(), new BBox(), new List() тощо)
        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            if (CurrentClass != null)
            {
                ITypeSymbol? targetType = null;
                var symbolInfo = _semanticModel.GetSymbolInfo(node);

                if (symbolInfo.Symbol is IMethodSymbol ctorSymbol)
                {
                    targetType = ctorSymbol.ContainingType;
                }
                else
                {
                    // Fallback для структур (BBox) та типів без явного конструктора
                    targetType = _semanticModel.GetTypeInfo(node).Type;
                }

                if (targetType != null)
                {
                    CheckAndAddDependency(CurrentClass, targetType, "Instantiates", $"new {targetType.Name}()");
                }
            }
            base.VisitObjectCreationExpression(node);
        }

        // 2.1 Неявні конструктори new(...)
        public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
        {
            if (CurrentClass != null)
            {
                var targetType = _semanticModel.GetTypeInfo(node).Type;
                if (targetType != null)
                {
                    CheckAndAddDependency(CurrentClass, targetType, "Instantiates", $"new {targetType.Name}()");
                }
            }
            base.VisitImplicitObjectCreationExpression(node);
        }

        // Допоміжний метод для точного відображення назв типів (включаючи масиви Color32[] та дженерики List<T>)
        private string GetTypeName(ITypeSymbol? type)
        {
            if (type == null) return "object";
            if (type is IArrayTypeSymbol arrayType)
                return GetTypeName(arrayType.ElementType) + "[]";
            if (type is INamedTypeSymbol namedType && namedType.IsGenericType)
            {
                var args = string.Join(", ", namedType.TypeArguments.Select(GetTypeName));
                return $"{namedType.Name}<{args}>";
            }
            return string.IsNullOrEmpty(type.Name) ? type.ToDisplayString() : type.Name;
        }

        // 3. Поля класу (ідеально чітке формулювання)
        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            if (CurrentClass == null) return;

            foreach (var variable in node.Declaration.Variables)
            {
                var fieldSymbol = _semanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
                if (fieldSymbol != null)
                {
                    string typeDisplay = GetTypeName(fieldSymbol.Type);
                    CheckAndAddDependency(CurrentClass, fieldSymbol.Type, "References", $"Field '{fieldSymbol.Name}' (holds '{typeDisplay}')");
                }
            }
            base.VisitFieldDeclaration(node);
        }

        // Допоміжний метод: додає Engine-залежність поточному класу + головному класу файлу (наприклад GrassSpawner для GrassRenderObject)
        private void AddEngineEdgeWithFilePrimaryFallback(string sourceClass, string targetEngineId, string edgeType, string details)
        {
            _graph.AddEdge(sourceClass, targetEngineId, edgeType, details);

            // Якщо клас знаходиться всередині іншого файлу (наприклад GrassSpawner.cs містить GrassRenderObject),
            // додаємо зв'язок також і для головного класу файлу!
            string fileNameClass = System.IO.Path.GetFileNameWithoutExtension(_filePath);
            if (!string.IsNullOrEmpty(fileNameClass) && !string.Equals(sourceClass, fileNameClass, StringComparison.OrdinalIgnoreCase) && _knownClasses.Contains(fileNameClass))
            {
                _graph.AddEdge(fileNameClass, targetEngineId, edgeType, details);
            }
        }

        // 4. Властивості класу
        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            if (CurrentClass == null) return;

            var propertySymbol = _semanticModel.GetDeclaredSymbol(node) as IPropertySymbol;
            if (propertySymbol != null)
            {
                string typeDisplay = GetTypeName(propertySymbol.Type);
                CheckAndAddDependency(CurrentClass, propertySymbol.Type, "References", $"Has property '{propertySymbol.Name}' (type: {typeDisplay})");
            }
            base.VisitPropertyDeclaration(node);
        }

        // 5. Параметри методів та конструкторів
        public override void VisitParameter(ParameterSyntax node)
        {
            if (CurrentClass != null)
            {
                var paramSymbol = _semanticModel.GetDeclaredSymbol(node);
                if (paramSymbol != null)
                {
                    CheckAndAddDependency(CurrentClass, paramSymbol.Type, "References", $"Parameter '{paramSymbol.Name}'");
                }
            }
            base.VisitParameter(node);
        }

        // 6. Виклики методів (підтримує Fluent API, статичні фабрики Material.Create, Texture.CreateRenderTarget, RenderTarget.From)
        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (CurrentClass != null)
            {
                var symbolInfo = _semanticModel.GetSymbolInfo(node);
                var methodSymbol = symbolInfo.Symbol as IMethodSymbol
                                  ?? (symbolInfo.CandidateSymbols.Length > 0 ? symbolInfo.CandidateSymbols[0] as IMethodSymbol : null);

                if (methodSymbol != null)
                {
                    var targetType = methodSymbol.IsExtensionMethod && methodSymbol.ReducedFrom != null
                        ? methodSymbol.ReducedFrom.ContainingType
                        : methodSymbol.ContainingType;

                    if (targetType != null)
                    {
                        string edgeType = IsSingleton(targetType) ? "CallsSingleton" : "Calls";
                        CheckAndAddDependency(CurrentClass, targetType, edgeType, $"Method '{methodSymbol.Name}()'");
                    }
                }
                else if (node.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    string methodName = memberAccess.Name.Identifier.Text;
                    var (callerType, isEngine) = ResolveCallerTypeOrName(memberAccess.Expression);

                    if (!string.IsNullOrEmpty(callerType))
                    {
                        if (isEngine)
                        {
                            string fullEngineName = callerType.StartsWith("Sandbox") ? callerType : "Sandbox." + callerType;
                            string engineId = EngineAnalyzer.GetUniqueId(fullEngineName);
                            AddEngineEdgeWithFilePrimaryFallback(CurrentClass, engineId, "Engine_Calls", $"Method '{methodName}()'");
                        }
                        else
                        {
                            _graph.AddEdge(CurrentClass, callerType, "Calls", $"Method '{methodName}()'");
                        }
                    }
                }
            }
            base.VisitInvocationExpression(node);
        }

        // 7. Звернення до властивостей та полів (Graphics.CameraPosition, Graphics.CameraRotation, Texture.Width тощо)
        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (CurrentClass != null)
            {
                // Пропускаємо виклики методів obj.Method(), вони опрацьовуються у VisitInvocationExpression
                if (node.Parent is InvocationExpressionSyntax)
                {
                    base.VisitMemberAccessExpression(node);
                    return;
                }

                var symbol = _semanticModel.GetSymbolInfo(node).Symbol;

                if (symbol is IFieldSymbol fieldSymbol)
                {
                    CheckAndAddDependency(CurrentClass, fieldSymbol.ContainingType, "References", $"Field '{fieldSymbol.Name}'");
                }
                else if (symbol is IPropertySymbol propertySymbol)
                {
                    if (propertySymbol.Name != "Instance")
                    {
                        CheckAndAddDependency(CurrentClass, propertySymbol.ContainingType, "References", $"Property '{propertySymbol.Name}'");
                    }
                }
                else if (node.Expression != null)
                {
                    string propName = node.Name.Identifier.Text;
                    var (callerType, isEngine) = ResolveCallerTypeOrName(node.Expression);

                    if (!string.IsNullOrEmpty(callerType) && isEngine)
                    {
                        string engineId = EngineAnalyzer.GetUniqueId("Sandbox." + callerType);
                        AddEngineEdgeWithFilePrimaryFallback(CurrentClass, engineId, "Engine_References", $"Property '{propName}'");
                    }
                }
            }
            base.VisitMemberAccessExpression(node);
        }


        private static readonly HashSet<string> SystemPrimitives = new(StringComparer.OrdinalIgnoreCase)
        {
            "String", "Int32", "Int64", "Single", "Double", "Boolean", "Object", "Char", "Byte",
            "Void", "Action", "Func", "Task", "Guid", "Array", "Type", "Decimal", "IntPtr",
            "ValueCollection", "KeyCollection", "Enumerator"
        };


        // Розумний резолвер, який розгортає Fluent API ланцюжки (Texture.CreateRenderTarget().WithSize()) 
        // та знаходить справжній тип об'єкта (DensityMask -> Texture, CanvasAttributes -> RenderAttributes)
        private (string? TypeName, bool IsEngine) ResolveCallerTypeOrName(ExpressionSyntax expr)
        {
            // 1. Перевіряємо семантичний тип (для змінних CanvasAttributes, DensityMask)
            var type = _semanticModel.GetTypeInfo(expr).Type
                      ?? (_semanticModel.GetSymbolInfo(expr).Symbol as IPropertySymbol)?.Type
                      ?? (_semanticModel.GetSymbolInfo(expr).Symbol as IFieldSymbol)?.Type;

            if (type != null && type.TypeKind != TypeKind.Error && !SystemPrimitives.Contains(type.Name) && type.TypeKind != TypeKind.TypeParameter)
            {
                bool isEngine = _filter.IsBlacklisted(type) || (type.ContainingNamespace?.ToDisplayString().StartsWith("Sandbox") == true);
                return (type.Name, isEngine);
            }

            // 2. Якщо це Fluent-ланцюжок (Texture.CreateRenderTarget().WithSize()) -> йдемо вглиб до першого об'єкта
            if (expr is InvocationExpressionSyntax innerInvocation && innerInvocation.Expression is MemberAccessExpressionSyntax innerMember)
            {
                return ResolveCallerTypeOrName(innerMember.Expression);
            }

            // 3. Якщо це просто ідентифікатор (Log, Input тощо) -> динамічно дізнаємося його справжній тип з api.json
            if (expr is IdentifierNameSyntax idSyntax)
            {
                string name = idSyntax.Identifier.Text;
                if (_filter.IsEngineType(name))
                {
                    string realType = _filter.ResolveEngineAlias(name);
                    return (realType, true);
                }
                if (_knownClasses.Contains(name)) return (name, false);
            }

            return (null, false);
        }



        private void CheckAndAddDependency(string sourceClass, ITypeSymbol? targetType, string edgeType, string details)
        {
            if (targetType == null) return;

            // 1. БЛОКУЄМО витік Generic-параметрів (T, U, TKey, GP_*) та системних примітивів (string, int, object)
            if (targetType.TypeKind == TypeKind.TypeParameter) return;
            if (SystemPrimitives.Contains(targetType.Name)) return;

            // 2. Масиви -> розпаковуємо елемент
            if (targetType is IArrayTypeSymbol arrayType)
            {
                CheckAndAddDependency(sourceClass, arrayType.ElementType, edgeType, details);
                return;
            }

            // 3. Дженерики (Dictionary<string, BrushBatch> -> розпаковуємо аргументи, але примітиви відсіються на кроці 1)
            if (targetType is INamedTypeSymbol namedType && namedType.IsGenericType && namedType.TypeArguments.Length > 0)
            {
                foreach (var arg in namedType.TypeArguments)
                {
                    CheckAndAddDependency(sourceClass, arg, edgeType, details);
                }
                return;
            }

            // 4. Обробка Error / Unresolved типів (Graphics, Material, Mesh, Texture, Log, MathX)
            if (targetType.TypeKind == TypeKind.Error)
            {
                string typeName = targetType.Name;
                if (string.IsNullOrEmpty(typeName) || typeName == "var" || typeName == "T" || typeName.StartsWith("GP_") || SystemPrimitives.Contains(typeName)) return;

                if (_knownClasses.Contains(typeName))
                {
                    _graph.AddEdge(sourceClass, typeName, edgeType, details + " (Fuzzy)");
                }
                else if (Filtering.TypeFilter.IncludeEngineLinks && _filter.IsEngineType(typeName))
                {
                    string engineId = EngineAnalyzer.GetUniqueId("Sandbox." + typeName);
                    _graph.AddEdge(sourceClass, engineId, "Engine_" + edgeType, details);
                }
                return;
            }

            // 5. Користувацькі локальні класи
            if (!_filter.IsBlacklisted(targetType))
            {
                string targetName = targetType.Name;
                if (!string.IsNullOrEmpty(targetName) && targetName != "T")
                {
                    _graph.AddEdge(sourceClass, targetName, edgeType, details);
                }
            }
            // 6. Офіційні класи Двигуна S&box / External (якщо це не системний примітив)
            else if (Filtering.TypeFilter.IncludeEngineLinks)
            {
                string ns = targetType.ContainingNamespace?.ToDisplayString() ?? "";
                if (ns.StartsWith("System") && !ns.Contains("Collections")) return; // Пропускаємо System.* примітиви

                string fqn = targetType.ToDisplayString().Split('<')[0];
                string uniqueEngineId = EngineAnalyzer.GetUniqueId(fqn);

                if (string.IsNullOrEmpty(uniqueEngineId) || uniqueEngineId == "void")
                {
                    uniqueEngineId = EngineAnalyzer.GetUniqueId("Sandbox." + targetType.Name);
                }

                _graph.AddEdge(sourceClass, uniqueEngineId, "Engine_" + edgeType, details);
            }
        }

        private bool IsSingleton(ITypeSymbol? type)
        {
            if (type == null) return false;
            foreach (var member in type.GetMembers("Instance"))
            {
                if (member.IsStatic) return true;
            }
            return false;
        }
    }
}