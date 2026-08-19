using Editor;
using Sandbox;

namespace SboxAstGraph.Editor;

// 1. Автоматична реєстрація у вкладках
[Dock("Editor", "AST Graph", "account_tree", DockArea.Right)]
public class AstGraphDock : Widget
{
    public AstGraphDock(Widget parent) : base(parent, false)
    {
        Layout = Layout.Column();
        Layout.Margin = 16;
        Layout.Spacing = 8;

        var title = Layout.Add(new Label("Sbox AST Graph Tool", this));
        title.SetStyles("font-size: 18px; font-weight: bold; color: #58a6ff;");

        var desc = Layout.Add(new Label("Інструмент аналізу архітектури коду успішно завантажено в s&box!", this));

        var btn = Layout.Add(new Button("Тестовий лог", this));
        btn.Clicked += () =>
        {
            Log.Info("AST Graph працює наживо всередині s&box Editor!");
        };

        Layout.AddStretchCell();
    }

    // 2. Кнопка у верхньому меню: Tools -> AST Graph
    [Menu("Editor", "Tools/AST Graph", "account_tree")]
    public static void OpenWindow()
    {
        var window = new AstGraphDock(null);
        window.Show();
    }
}