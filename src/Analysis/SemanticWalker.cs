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
        private readonly HashSet<string> _knownClasses; // ДОДАНО ПОЛЕ

        // Стек для відстеження поточного класу (враховує вкладені класи)
        private readonly Stack<string> _classContextStack = new();

        public SemanticWalker(SemanticModel semanticModel, TypeFilter filter, CodeGraph graph, string filePath, HashSet<string> knownClasses)
        {
            _semanticModel = semanticModel;
            _filter = filter;
            _graph = graph;
            _filePath = filePath;
            _knownClasses = knownClasses; // ДОДАНО ІНІЦІАЛІЗАЦІЮ
        }

        private string? CurrentClass => _classContextStack.Count > 0 ? _classContextStack.Peek() : null;

        // 1. Оголошення класу та його наслідування (Вершина графу + зв'язок Inherits)
        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var classSymbol = _semanticModel.GetDeclaredSymbol(node);

            if (classSymbol != null && !_filter.IsBlacklisted(classSymbol))
            {
                string className = classSymbol.Name;
                string ns = classSymbol.ContainingNamespace?.ToDisplayString() ?? "SboxGeneratedRazorSpace";

                // Додаємо клас у граф як вершину
                _graph.AddNode(className, _filePath, ns);

                // ПЕРЕВІРКА НАСЛІДУВАННЯ: Чи наслідується клас від іншого нашого класу?
                var baseType = classSymbol.BaseType;
                if (baseType != null && !_filter.IsBlacklisted(baseType))
                {
                    _graph.AddEdge(className, baseType.Name, "Inherits", "Base Class");
                }

                // Заходимо в контекст цього класу
                _classContextStack.Push(className);

                base.VisitClassDeclaration(node);

                // Виходимо з контексту цього класу
                _classContextStack.Pop();
            }
            else
            {
                base.VisitClassDeclaration(node);
            }
        }

        // 2. Поля класу (Зв'язок "References")
        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            if (CurrentClass == null) return;

            foreach (var variable in node.Declaration.Variables)
            {
                var fieldSymbol = _semanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
                if (fieldSymbol != null)
                {
                    CheckAndAddDependency(CurrentClass, fieldSymbol.Type, "References", $"Field: {fieldSymbol.Name}");
                }
            }
            base.VisitFieldDeclaration(node);
        }

        // 3. Властивості класу (Зв'язок "References")
        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            if (CurrentClass == null) return;

            var propertySymbol = _semanticModel.GetDeclaredSymbol(node) as IPropertySymbol;
            if (propertySymbol != null)
            {
                CheckAndAddDependency(CurrentClass, propertySymbol.Type, "References", $"Property: {propertySymbol.Name}");
            }
            base.VisitPropertyDeclaration(node);
        }

        // 4. Виклики методів та прямий запуск подій/Action (наприклад, OnEvent())
        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (CurrentClass != null)
            {
                var symbolInfo = _semanticModel.GetSymbolInfo(node);
                if (symbolInfo.Symbol is IMethodSymbol methodSymbol)
                {
                    // Якщо це прямий запуск події/Action (Delegate Invoke)
                    if (methodSymbol.MethodKind == MethodKind.DelegateInvoke)
                    {
                        var eventSymbol = _semanticModel.GetSymbolInfo(node.Expression).Symbol;
                        if (eventSymbol != null)
                        {
                            var targetType = eventSymbol.ContainingType;
                            CheckAndAddDependency(CurrentClass, targetType, "Triggers", $"Action/Event: {eventSymbol.Name}");
                        }
                    }
                    else // Звичайний виклик методу
                    {
                        var targetType = methodSymbol.ContainingType;
                        if (targetType != null)
                        {
                            // Якщо клас є синглтоном, ставимо тип зв'язку CallsSingleton
                            string edgeType = IsSingleton(targetType) ? "CallsSingleton" : "Calls";
                            CheckAndAddDependency(CurrentClass, targetType, edgeType, $"Method: {methodSymbol.Name}()");
                        }
                    }
                }
            }
            base.VisitInvocationExpression(node);
        }

        // 4.1 Виклик подій через умовний доступ (наприклад, Event?.Invoke())
        public override void VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
        {
            if (CurrentClass != null)
            {
                // Перевіряємо, чи права частина — це запуск методу Invoke
                if (node.WhenNotNull is InvocationExpressionSyntax ||
                    (node.WhenNotNull is MemberBindingExpressionSyntax binding && binding.Name.Identifier.Text == "Invoke"))
                {
                    // Отримуємо символ самої події (ліва частина перед ?. )
                    var leftSymbol = _semanticModel.GetSymbolInfo(node.Expression).Symbol;

                    if (leftSymbol != null)
                    {
                        var targetType = leftSymbol.ContainingType;
                        if (leftSymbol is IEventSymbol eventSymbol)
                        {
                            CheckAndAddDependency(CurrentClass, targetType, "Triggers", $"Event: {eventSymbol.Name}");
                        }
                        else if (leftSymbol is IFieldSymbol fieldSymbol && fieldSymbol.Type.Name.Contains("Action"))
                        {
                            CheckAndAddDependency(CurrentClass, targetType, "Triggers", $"Action: {fieldSymbol.Name}");
                        }
                    }
                }
            }
            base.VisitConditionalAccessExpression(node);
        }

        // 5. Підписка на події (OnEvent += HandleEvent)
        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            // Нас цікавить лише оператор "+="
            if (CurrentClass != null && node.Kind() == SyntaxKind.AddAssignmentExpression)
            {
                var leftSymbol = _semanticModel.GetSymbolInfo(node.Left).Symbol;

                // Перевіряємо, чи це подія (Event) або C# делегат/Action
                if (leftSymbol is IEventSymbol eventSymbol)
                {
                    var targetType = eventSymbol.ContainingType;
                    CheckAndAddDependency(CurrentClass, targetType, "Subscribes", $"Event: {eventSymbol.Name}");
                }
                else if (leftSymbol is IFieldSymbol fieldSymbol && fieldSymbol.Type.Name.Contains("Action"))
                {
                    var targetType = fieldSymbol.ContainingType;
                    CheckAndAddDependency(CurrentClass, targetType, "Subscribes", $"Action: {fieldSymbol.Name}");
                }
            }
            base.VisitAssignmentExpression(node);
        }

        // 6. Звернення до полів та властивостей інших класів усередині методів (наприклад, manager._renderCount)
        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (CurrentClass != null)
            {
                var symbol = _semanticModel.GetSymbolInfo(node).Symbol;

                if (symbol is IFieldSymbol fieldSymbol)
                {
                    var targetType = fieldSymbol.ContainingType;
                    string edgeType = IsSingleton(targetType) ? "ReferencesSingleton" : "References";
                    CheckAndAddDependency(CurrentClass, targetType, edgeType, $"Field: {fieldSymbol.Name}");
                }
                else if (symbol is IPropertySymbol propertySymbol)
                {
                    // Ігноруємо технічну властивість "Instance", щоб не створювати зв'язок класу на самого себе
                    if (propertySymbol.Name != "Instance")
                    {
                        var targetType = propertySymbol.ContainingType;
                        string edgeType = IsSingleton(targetType) ? "ReferencesSingleton" : "References";
                        CheckAndAddDependency(CurrentClass, targetType, edgeType, $"Property: {propertySymbol.Name}");
                    }
                }
            }
            base.VisitMemberAccessExpression(node);
        }

        /// <summary>
        /// Допоміжний метод для перевірки типу через фільтр та додавання зв'язку в граф.
        /// </summary>
        private void CheckAndAddDependency(string sourceClass, ITypeSymbol? targetType, string edgeType, string details)
        {
            if (targetType == null) return;

            // Якщо Roslyn не зміг розпізнати тип через помилки компіляції (Error Type)
            if (targetType.TypeKind == TypeKind.Error)
            {
                string typeName = targetType.Name;
                // Спробуємо знайти назву типу серед наших відомих кастомних класів (Fuzzy fallback)
                if (_knownClasses.Contains(typeName))
                {
                    _graph.AddEdge(sourceClass, typeName, edgeType, details + " (Fuzzy)");
                    return;
                }
            }

            // 1. Розпаковуємо масиви (наприклад, SwarmUnit[] -> беремо SwarmUnit)
            if (targetType is IArrayTypeSymbol arrayType)
            {
                CheckAndAddDependency(sourceClass, arrayType.ElementType, edgeType, details);
                return;
            }

            // 2. Відсікаємо дженерики (наприклад, List<Player> -> беремо саме Player)
            if (targetType is INamedTypeSymbol namedType && namedType.IsGenericType && namedType.TypeArguments.Length > 0)
            {
                foreach (var arg in namedType.TypeArguments)
                {
                    CheckAndAddDependency(sourceClass, arg, edgeType, details);
                }
                return;
            }

            // 3. Якщо цільовий тип не в чорному списку примітивів/рушія
            if (!_filter.IsBlacklisted(targetType))
            {
                string targetName = targetType.Name;

                if (!string.IsNullOrEmpty(targetName))
                {
                    _graph.AddEdge(sourceClass, targetName, edgeType, details);
                }
            }
            // --- ОПЦІОНАЛЬНИЙ ЗБІР ЗВ'ЯЗКІВ ДВИГУНА ---
            else if (Filtering.TypeFilter.IncludeEngineLinks)
            {
                string ns = targetType.ContainingNamespace?.ToDisplayString() ?? "";
                if (ns.StartsWith("Sandbox") || ns.StartsWith("Editor"))
                {
                    // Розумно вирішуємо ім'я лінку (Sandbox.PanelComponent або просто Vector3)
                    string fqn = _filter.GetEngineFqn(targetType);
                    if (!string.IsNullOrEmpty(fqn))
                    {
                        _graph.AddEdge(sourceClass, fqn, "Engine_" + edgeType, details);
                    }
                }
            }
        }

        private bool IsSingleton(ITypeSymbol? type)
        {
            if (type == null) return false;

            // Перевіряємо, чи є в класі статичне поле або властивість з назвою "Instance"
            foreach (var member in type.GetMembers("Instance"))
            {
                if (member.IsStatic) return true;
            }
            return false;
        }
    }
}