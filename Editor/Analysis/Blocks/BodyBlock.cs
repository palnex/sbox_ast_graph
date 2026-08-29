#nullable enable
using System.Collections.Generic;

namespace Editor.Analysis.Models.Blocks;

/// <summary>
/// Primary static anatomy block detailing identity, ECMA DocId, display info, and network realm.
/// </summary>
public class BodyBlock
{
    /// <summary> Dynamic package / library identity (e.g. "mygame", "sbox_ast_graph"). </summary>
    public string PackageName { get; set; } = string.Empty;

    /// <summary> Unique ECMA-334 DocId (e.g. "T:Sandbox.Component", "M:MyGame.Player.Attack(DamageInfo)"). </summary>
    public string DocId { get; set; } = string.Empty;

    public string Id => DocId;
    public string Name { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;

    public NodeOrigin Origin { get; set; } = NodeOrigin.UserProject;
    public SandboxTypeCategory Category { get; set; } = SandboxTypeCategory.Class;
    public NetworkRealm Realm { get; set; } = NetworkRealm.Shared;

    public string? FilePath { get; set; }
    public int LineNumber { get; set; }

    public bool IsAbstract { get; set; }
    public bool IsStatic { get; set; }
    public bool IsValueType { get; set; }
    public bool IsAsync { get; set; }

    public string ReturnTypeName { get; set; } = "void";
    public List<ParameterItem> Parameters { get; set; } = new();

    public bool IsUserCode => Origin == NodeOrigin.UserProject;
    public bool IsEngineCode => Origin is NodeOrigin.EngineRuntime or NodeOrigin.EngineEditor;
}