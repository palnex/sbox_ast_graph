namespace Editor.Analysis.Models;

/// <summary>
/// Origin source of the analyzed entity.
/// </summary>
public enum NodeOrigin
{
    UserProject,
    EngineRuntime,
    EngineEditor,
    SystemPrimitive,
    External
}

/// <summary>
/// Functional architecture role in s&box.
/// </summary>
public enum SandboxTypeCategory
{
    SceneComponent,
    UiPanel,
    UiPanelComponent,
    GameResource,
    Class,
    Struct,
    Interface,
    Enum
}

/// <summary>
/// Fractal hierarchy scale level.
/// </summary>
public enum FractalLevel
{
    Project,
    Module,
    File,
    Class,
    Member
}

/// <summary>
/// Network execution realm constraint.
/// </summary>
public enum NetworkRealm
{
    Shared,
    HostOnly,
    ClientOnly,
    RpcBroadcast
}

/// <summary>
/// Directed semantic action connecting two entities.
/// </summary>
public enum RelationKind
{
    /// <summary> Class inheritance (A : B). </summary>
    Inherits,

    /// <summary> Interface contract implementation (A : ISomething). </summary>
    Implements,

    /// <summary> Polymorphic fan-out link from interface to concrete implementing class. </summary>
    PolymorphicDispatch,

    /// <summary> Direct object instantiation (new MyClass()). </summary>
    Instantiates,

    /// <summary> Field holding an object reference. </summary>
    FieldReference,

    /// <summary> Property declaration reference. </summary>
    PropertyReference,

    /// <summary> Synchronous direct method call. </summary>
    MethodCall,

    /// <summary> Asynchronous suspension / task continuation (await Task / async). </summary>
    AsyncAwait,

    /// <summary> Network boundary transmission ([Rpc.Broadcast], [Rpc.Host]). </summary>
    RpcDispatch,

    /// <summary> Static or Singleton accessor (.Instance, .Current). </summary>
    SingletonAccess,

    /// <summary> Dynamic scene component lookup (Components.Get&lt;T&gt;()). </summary>
    ComponentFetch,

    /// <summary> Event or Action delegate subscription (+= / -=). </summary>
    EventSubscription,

    /// <summary> Razor UI markup component tag (&lt;CustomWidget /&gt;). </summary>
    RazorMarkupTag,

    /// <summary> Generic type argument unwrapping (List&lt;T&gt; -&gt; T). </summary>
    GenericArgument
}