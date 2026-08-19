using System;
using System.Collections.Generic;

namespace SboxAstGraph.Model
{
    public class TypeSignature
    {
        public string RawName { get; set; } = string.Empty;       // Повний початковий рядок (наприклад, "List<Sandbox.Component>")
        public string FullName { get; set; } = string.Empty;      // Ім'я без модифікаторів та дженериків (наприклад, "System.Collections.Generic.List")
        public string CleanName { get; set; } = string.Empty;     // Коротке ім'я типу (наприклад, "List")
        public bool IsArray { get; set; }                         // Чи є масивом ([])
        public bool IsPointer { get; set; }                       // Чи є покажчиком (*)
        public bool IsByRef { get; set; }                         // Чи передається по посиланню (@ / ref / out)

        // Аргументи дженерика (для List<Component> тут лежатиме сигнатура Component)
        public List<TypeSignature> GenericArguments { get; } = new();
    }
}