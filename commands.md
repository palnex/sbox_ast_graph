dotnet run -- --mode user --src "C:/Users/yenro/Desktop/s&box-my-games/towertinno" --api "C:/Users/yenro/Desktop/sbox_ast_graph/api.json" --out "C:/Users/yenro/Desktop/Personal - Agents - Memory/sbox/library" --engine-links

dotnet run -- --mode engine --api "C:/Users/yenro/Desktop/sbox_ast_graph/api.json" --out "C:/Users/yenro/Desktop/Personal - Agents - Memory/sbox/engine_library"

taskkill /F /IM python.exe

dotnet build

dotnet build -c Release