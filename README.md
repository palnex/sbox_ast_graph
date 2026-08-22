

# SboxAstGraph






> **MAJOR UPDATE: Migrated to Native S&box Editor Library & GPU Visualizer Engine!**
> 
> The project has evolved from a standalone CLI utility into a **high-performance, native S&box Editor Library (`.sbproj`)**. 
> It now features live in-memory `Sandbox.TypeLibrary` and Roslyn AST integration, real-time 3D Orbit / 2D Ortho camera navigation, and a hardware-accelerated GPU instanced node visualizer.

<video src="https://github.com/user-attachments/assets/037daee8-10b5-4211-accf-e009c5ffc75a" autoplay loop muted playsinline width="60%"></video>

---

### Legacy CLI Documentation (Archived)
*The documentation below reflects the original standalone CLI / mock-assembly version and is preserved for development history and Facepunch application review.*

---


A specialized, lightweight static code analysis tool, virtual SDK generator, dependency graph builder, and Model Context Protocol (MCP) server designed specifically for the S&box engine ecosystem (.NET 10 / Source 2).

> **Alpha Disclaimer & Note for Facepunch**:
> This repository is a work-in-progress published to showcase my current ideas, experiments, and progress for my job application to **Facepunch Studios**.
> 
> Please note that even this high-level architectural overview and documentation do not 100% reflect the exact current state of the codebase yet. I am actively testing, refactoring, and debugging the parser, MCP integration, and AI logic.
> 
> If you encounter any ambiguities while reviewing the repository, feeding the codebase into an AI assistant will give you a live breakdown of the current implementation.
> 
> Hi Facepunch team :3

---
---

## 1. Current Project Status & Vision

### Status & Limitations
This utility is an experimental Alpha release. Certain components currently rely on hardcoded heuristic rules and schema transformations.

* **In-Memory Assembly Generation**: The tool leverages `Facepunch.AssemblySchema` to dynamically build in-memory C# Reference Assemblies (`.dll`) directly via Roslyn metadata references from `api.json`. This solves missing assembly errors (CS0246/CS0234) without needing local binary distributions of the engine.
* **Parsing Verification**: The parser and semantic walker cover major C# and Razor usage patterns, but full parsing coverage across all complex user codebases is not 100% guaranteed yet. Further broad testing, edge-case handling, and debugging are ongoing.

### Unified Vision: MCP Expansion & S&box Editor Integration
The query capabilities of SboxAstGraph (`find_path`, `check_cycles`, `get_metrics`, and semantic search) form a unified engine. Currently, these capabilities are being expanded along two parallel tracks that share the same backend:

1. **Native S&box Editor Tool**: Packaging the engine as an integrated S&box Editor Tab. This will allow developers to visually inspect class connections, read API summaries, and navigate code relationships directly inside the editor while coding.
2. **Deep AI Context via MCP**: Exposing graph-level pathfinding, cycle detection, and metrics directly to AI coding agents. Both the human developer (via the Editor Tab) and the AI agent (via MCP) consume the exact same underlying graph data. This replaces slow web-scraping solutions (such as `sbox-mcp-documentation`) with instant, local, in-editor context.
3. **Future Engine Reflection**: Future versions aim to complement static `api.json` stubbing with direct reflection against the active S&box engine process.

---

## 2. Problems Addressed

Standard C# dependency graph generators and generic AST parsers face several hurdles when applied to S&box projects:

1. **Engine Noise**: Generic analyzers treat standard engine primitives, UI components, and collections (`Vector3`, `Component`, `Panel`, `List<T>`) as primary graph nodes, resulting in unreadable "spaghetti" graphs.
2. **S&box Specific Mechanics**: Generic tools fail to resolve custom semantic patterns, such as singleton access chains (`GameManager.Instance.Property`), C# Action subscriptions (`+=`), invocation delegates (`?.Invoke()`), and inline Razor tag expressions (`<UpgradeNode />`, `@Formulas.Method()`).
3. **Missing Engine Assemblies**: Standard Roslyn semantic analysis breaks without access to official engine binaries (`Sandbox.Game.dll`, `Sandbox.UI.dll`), which are not hosted on public NuGet feeds.
4. **Stub Maintenance**: Manually maintaining mock assemblies for evolving engine APIs leads to breaking signature mismatches.

SboxAstGraph solves these issues by compiling the official S&box `api.json` schema into an in-memory `.dll` reference assembly using a self-healing compiler loop, separating heavy code analysis from fast query execution and AI context retrieval.

---

## 3. Architecture & CLI Arguments

The utility is controlled via CLI arguments:

### Primary Execution Modes (`--mode`)
* `--mode user`: Recursively scans `.cs` and `.razor` files in the user's game project directory. Generates a filtered dependency graph (`graph.json`), an Obsidian Canvas visualization (`graph.canvas`), and structured Markdown documentation per class.
* `--mode engine`: Parses metadata from `api.json` and exports structured Markdown documentation for engine types, methods, fields, and properties.
* `--mode both`: Executes both User Code Analysis and Engine API Documentation sequentially.
* `--mode mcp`: Launches the stdio-based MCP server for AI coding assistants.

### Key CLI Parameters
* `--src "<path>"`: Path to the target user game project source folder.
* `--out "<path>"`: Output directory for generated graphs, vector indexes, and Markdown files.
* `--api "<path>"`: Path to the local `api.json` schema file.
* `--engine-links`: Optional flag. When enabled, preserves direct dependency links from User Code nodes to S&box Engine API nodes in the graph instead of stripping them.

---

## Recommended Visualization Workflow: Obsidian Graph View

The exported output directory (`--out`) is structured to function natively as an **Obsidian Vault**.

For the best visual experience, open the output directory in Obsidian and use the native **Graph View** (rather than opening `.canvas` files). Because every user class and engine type is exported as an interconnected Markdown note (`.md`) with wikilinks (`[[ClassName]]`), Obsidian's native Graph View automatically renders a clean, dynamic, and interactive dependency map of your codebase.

---

## 4. Model Context Protocol (MCP) Integration

When launched with `--mode mcp`, SboxAstGraph acts as a language-model context provider over stdio.

### Client Configuration Example
Add the compiled executable to your MCP settings file (e.g., Cursor, Claude Desktop, or Windsurf):

```json
{
  "mcpServers": {
    "sbox-ast-graph": {
      "command": "C:/PathTo/sbox_ast_graph/bin/Release/net10.0/SboxAstGraph.exe",
      "args": [
        "--mode", "mcp",
        "--src", "C:/PathTo/YourSboxGameProject",
        "--api", "C:/PathTo/sbox_ast_graph/api.json",
        "--out", "C:/PathTo/OutputLibrary"
      ],
      "cwd": "C:/PathTo/sbox_ast_graph"
    }
  }
}
```

### Currently Implemented Base Tools
1. `sbox_engine_search_api`: Searches the official S&box Engine API using keyword matching, semantic vector matching, or a hybrid strategy.
2. `sbox_engine_explain`: Retrieves full or sectioned API documentation for any S&box engine type, including member signatures and description summaries.
3. `sbox_user_semantic_search`: Performs vector RAG search over the user's local game project codebase.
4. `sbox_user_explain_class`: Returns incoming/outgoing code dependencies and engine API usages for a specified user class.

### MCP Tool Expansion Plans (Tied to In-Editor Debugging)
The MCP toolset is currently expanding to expose the full analytical power of the C# `QueryEngine` to AI agents:
* `sbox_user_find_path`: Exposing graph pathfinding to allow AI to analyze call chains and execution routes between arbitrary user components.
* `sbox_user_check_cycles`: Exposing circular dependency detection to help AI catch architecture loops during refactoring.
* `sbox_user_get_metrics`: Exposing class weight metrics so AI agents can identify god-objects or dead code.

---

## 5. Hybrid AI Engine (Librarian AI)

In addition to Roslyn AST parsing, SboxAstGraph integrates a local Python-based vector search service (`librarian_ai`).

* **Embeddings Model**: Utilizes `ibm-granite-97m` (384-dimensional embeddings) to index both user code summaries and S&box engine API documentation.
* **Quantized Vector Index**: Uses 4-bit TurboVec quantization (`turbovec.IdMapIndex`) paired with binary `.pkl` caching, enabling sub-100ms semantic retrieval across 15,000+ API and code nodes.
* **Automatic Process Lifecycle**: The C# runtime (`LibrarianClient.cs`) automatically detects if the Python daemon (`librarian_service.py`) is offline, spawns the process in the background, and handles process lifecycle management.
* **Idle Shutdown**: The local AI service automatically shuts down after 30 minutes of inactivity to preserve system memory.

---

## 6. CLI Query Engine

For quick manual inspection without Roslyn compilation overhead, SboxAstGraph includes a CLI query mode operating directly on pre-built `graph.json` files:

### Pathfinding (`--cmd path`)
Finds the shortest dependency route between two classes using BFS.
```bash
dotnet run -- --cmd path --arg1 ProgressionMath --arg2 SwarmManager --out "./output_test" --undirected
```

### Circular Dependency Detection (`--cmd cycles`)
Identifies circular reference loops in the codebase.
```bash
dotnet run -- --cmd cycles --out "./output_test"
```

### Class Inspection (`--cmd explain`)
Outputs namespace, source location, incoming dependencies, and outgoing dependencies for a class.
```bash
dotnet run -- --cmd explain --arg1 StatRegistry --out "./output_test"
```

### Metrics & Weight Analysis (`--cmd metrics`)
Calculates graph hubness, outgoing weight, and isolated nodes.
```bash
dotnet run -- --cmd metrics --out "./output_test"
```

### Semantic Search (`--cmd search`)
Runs a semantic vector query against the local codebase via `librarian_ai`.
```bash
dotnet run -- --cmd search --arg1 "player movement acceleration" --out "./output_test"
```

---

## 7. Configuration & File Filtering

The analyzer supports `.astignore` files placed in the project root to exclude specific folders or files from parsing (similar to `.gitignore`):

```gitignore
# Test directories
Tests/
ThirdParty/

# Specific files or patterns
temp_api_stub.cs
*.designer.cs
```

---

## 8. Prerequisites & Setup

### Environment Requirements
* **SDK**: .NET 10 SDK (or .NET 8).
* **Python**: Python 3.10+ (required for the `librarian_ai` service).
* **NuGet Packages**:
  * `Microsoft.CodeAnalysis.CSharp` (Roslyn compiler platform).
  * `Facepunch.AssemblySchema` (S&box API schema library).

### Building & Running

1. **Download AI Model Weights**:
   ```bash
   python librarian_ai/download_model.py
   ```

2. **Build the Project**:
   ```bash
   dotnet build -c Release
   ```

3. **Run Code Analysis**:
   ```bash
   dotnet run -- --src "C:/PathToYourSboxGame" --out "./output_test" --mode user --engine-links
   ```
