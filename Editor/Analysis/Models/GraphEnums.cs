namespace Editor.Analysis.Models;

/// <summary>
/// Defines where the analyzed type originates from.
/// </summary>
public enum NodeOrigin
{
    /// <summary>
    /// Code written inside the active user project.
    /// </summary>
    UserProject,

    /// <summary>
    /// Core s&box runtime engine (Sandbox.*, Facepunch.*).
    /// </summary>
    EngineRuntime,

    /// <summary>
    /// s&box Editor tools and framework (Editor.*).
    /// </summary>
    EngineEditor,

    /// <summary>
    /// Standard .NET runtime primitives (System.*, Microsoft.*).
    /// </summary>
    SystemPrimitive,

    /// <summary>
    /// External third-party libraries or packages.
    /// </summary>
    External
}

/// <summary>
/// Categorizes the functional role of the type within s&box architecture.
/// </summary>
public enum SandboxTypeCategory
{
    /// <summary>
    /// Scene system component inheriting from Sandbox.Component.
    /// </summary>
    SceneComponent,

    /// <summary>
    /// UI Panel inheriting from Sandbox.UI.Panel.
    /// </summary>
    UiPanel,

    /// <summary>
    /// Scene component hosting UI inheriting from Sandbox.UI.PanelComponent.
    /// </summary>
    UiPanelComponent,

    /// <summary>
    /// Data asset inheriting from Sandbox.GameResource.
    /// </summary>
    GameResource,

    /// <summary>
    /// Standard C# class.
    /// </summary>
    Class,

    /// <summary>
    /// Value type or struct.
    /// </summary>
    Struct,

    /// <summary>
    /// Interface contract.
    /// </summary>
    Interface,

    /// <summary>
    /// Enumeration type.
    /// </summary>
    Enum
}

/// <summary>
/// Describes the nature of the dependency relationship between two nodes.
/// </summary>
public enum RelationKind
{
    /// <summary>
    /// Class inheritance (A : B).
    /// </summary>
    Inherits,

    /// <summary>
    /// Interface implementation (A : ISomething).
    /// </summary>
    Implements,

    /// <summary>
    /// Object instantiation (new MyClass()).
    /// </summary>
    Instantiates,

    /// <summary>
    /// Direct field reference.
    /// </summary>
    FieldReference,

    /// <summary>
    /// Property declaration of target type.
    /// </summary>
    PropertyReference,

    /// <summary>
    /// Method call or invocation.
    /// </summary>
    MethodCall,

    /// <summary>
    /// Accessing a singleton or static accessor (.Instance, .Current).
    /// </summary>
    SingletonAccess,

    /// <summary>
    /// Accessing component via Components.Get&lt;T&gt;() or GetComponent&lt;T&gt;().
    /// </summary>
    ComponentFetch,

    /// <summary>
    /// Event or Action subscription (+= / -=).
    /// </summary>
    EventSubscription,

    /// <summary>
    /// Usage of a custom UI component inside .razor markup tags (e.g. &lt;CustomWidget /&gt;).
    /// </summary>
    RazorMarkupTag,

    /// <summary>
    /// Dependency extracted from generic argument (e.g. List&lt;T&gt; -> T).
    /// </summary>
    GenericArgument
}