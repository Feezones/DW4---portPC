using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using DW4.Formats.Afs;
using DW4.Formats.Igb;

/// <summary>
/// dw4 colisao-auditoria: procura "paredes invisíveis" nas fases já exportadas (fase.json de dw4 fase-exportar). Só lê.
/// Para cada fase, sobre a raiz de colisão (BG01) que o cliente carrega:
///  - paredes sem cenário visível perto (nenhum triângulo das raízes BG02 a menos de <see cref="CoverDist"/> unidades);
///  - peças sob igSwitch com o bit do filho desligado no arquivo (o jogo não as liga por padrão);
///  - beHitLandModelInfo*/beHitLandDelivInfo* com todos os campos crus e as peças de colisão abaixo deles;
///  - degraus baixos (paredes de até <see cref="StepMax"/> de altura) e rampas íngremes (mais de 45°, que a Godot trata como parede).
/// Grava auditoria.json e auditoria.md numa pasta fora do repositório.
/// </summary>
static class ColisaoAuditoria
{
    const float CoverDist = 6f;     // distância máxima de uma amostra da parede até o cenário para contar como "vista"
    const float SampleStep = 8f;    // espaçamento das amostras sobre cada triângulo
    const float StepMax = 6f;       // parede mais baixa que isso é degrau
    const float Cell = 32f;         // grade XZ do cenário

    sealed record Tri(Vector3 A, Vector3 B, Vector3 C, int Geometry)
    {
        public Vector3 Normal { get { var n = Vector3.Cross(B - A, C - A); float l = n.Length(); return l > 1e-9f ? n / l : Vector3.Zero; } }
        public float Area => Vector3.Cross(B - A, C - A).Length() / 2;
        public Vector3 Min => Vector3.Min(A, Vector3.Min(B, C));
        public Vector3 Max => Vector3.Max(A, Vector3.Max(B, C));
    }

    public static int Run(string ndmwPath, string fasesDir, string outDir, string[] only)
    {
        var full = Path.GetFullPath(outDir);
        for (var d = new DirectoryInfo(full); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, ".git")) || File.Exists(Path.Combine(d.FullName, ".git")))
            { Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git"); return 1; }
        Directory.CreateDirectory(full);
        var stages = Directory.GetDirectories(fasesDir).Select(d => Path.GetFileName(d))
            .Where(s => File.Exists(Path.Combine(fasesDir, s, "fase.json")))
            .Where(s => only.Length == 0 || only.Contains(s, StringComparer.OrdinalIgnoreCase))
            .OrderBy(s => s, StringComparer.Ordinal).ToList();
        using var afs = AfsArchive.Open(ndmwPath);
        var all = new JsonArray();
        var md = new StringBuilder();
        md.AppendLine("# Auditoria da colisão (dw4 colisao-auditoria)");
        md.AppendLine();
        md.AppendLine($"Parede = triângulo com |normal.y| < 0,5. \"Sem cenário\" = nenhuma amostra a menos de {CoverDist} unidades de um triângulo das raízes BG02. Degrau = parede de até {StepMax} de altura. Rampa = 45° a 70°.");
        md.AppendLine();
        md.AppendLine("| Fase | Triângulos | Paredes sem cenário (área) | Regiões | Peças com switch desligado | HitLand | Degraus | Rampas |");
        md.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var stage in stages)
        {
            try
            {
                var j = Stage(afs, Path.Combine(fasesDir, stage, "fase.json"), stage, md);
                if (j != null) all.Add(j);
            }
            catch (Exception ex) { Console.Error.WriteLine($"{stage}: {ex.Message}"); md.AppendLine($"| {stage} | erro: {ex.Message} | | | | | | |"); }
        }
        File.WriteAllText(Path.Combine(full, "auditoria.json"), all.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(full, "auditoria.md"), md.ToString());
        Console.WriteLine($"{stages.Count} fases -> {full}");
        return 0;
    }

    static JsonObject? Stage(AfsArchive afs, string faseJson, string stage, StringBuilder md)
    {
        var fase = JsonNode.Parse(File.ReadAllText(faseJson))!;
        int main = fase["entradas"]?["principal"]?["entrada"]?.GetValue<int>() ?? -1;
        int bg01 = fase["raizes"]?["colisao_BG01"]?.GetValue<int>() ?? -1;
        if (main < 0 || bg01 < 0) { md.AppendLine($"| {stage} | sem colisão | | | | | | |"); return null; }
        using var ms = new MemoryStream();
        afs.CopyEntry(afs.Entries[main], ms);
        var data = ms.ToArray();
        var file = IgbFile.Read(data);
        bool le = file.Header.LittleEndian;
        var scene = IgbScene.Read(file, data);
        string NameOf(int idx) => idx >= 0 && idx < file.Slots.Count && file.Slots[idx] is IgbObject o ? o.Fields.FirstOrDefault(v => v.Desc.Slot == 2 && v.Desc.ShortName == "String")?.Text ?? "" : "";
        IgbObject? Ref(IgbObject o, int slot) { var v = o.Fields.FirstOrDefault(x => x.Desc.Slot == slot && x.Desc.ShortName == "ObjectRef"); int i = v?.AsInt32(le) ?? -1; return i >= 0 && i < file.Slots.Count ? file.Slots[i] as IgbObject : null; }
        IEnumerable<IgbObject> Children(IgbObject node)
        {
            foreach (var v in node.Fields.Where(x => x.Desc.ShortName == "ObjectRef"))
            {
                int i = v.AsInt32(le);
                if (i < 0 || i >= file.Slots.Count || file.Slots[i] is not IgbObject r) continue;
                if (file.Is(r, "igObjectList")) foreach (var item in scene.ObjectList(r).Where(x => file.Is(x, "igNode"))) yield return item;
                else if (file.Is(r, "igNode")) yield return r;
            }
        }

        // Marcadores dos pontos de entrada (como o igb-colisao): fora.
        var markers = new HashSet<int>();
        void Collect(IgbObject n, int depth)
        {
            if (depth > 16) return;
            if (file.Is(n, "igGeometry")) foreach (var ga in scene.AttrList(n).Where(a => file.Is(a, "igGeometryAttr"))) markers.Add(ga.Index);
            foreach (var c in Children(n)) Collect(c, depth + 1);
        }
        foreach (var pi in file.Slots.OfType<IgbObject>().Where(o => o.TypeName == "bePoint01Info"))
            if (Ref(pi, 7) is { } sw) Collect(sw, 0);

        // Caminho de igSwitch de cada geometria da colisão: "sw#N[k]=liga/desliga".
        var switchPath = new Dictionary<int, List<string>>();
        var offGeoms = new HashSet<int>();
        void Walk(IgbObject node, List<string> path, bool off, HashSet<int> seen)
        {
            if (!seen.Add(node.Index)) return;
            if (file.Is(node, "igAttrSet"))
                foreach (var ga in scene.AttrList(node).Where(a => file.Is(a, "igGeometryAttr")))
                {
                    switchPath[ga.Index] = [.. path];
                    if (off) offGeoms.Add(ga.Index);
                }
            if (file.Is(node, "igGeometry"))
                foreach (var ga in scene.AttrList(node).Where(a => file.Is(a, "igGeometryAttr")))
                {
                    switchPath[ga.Index] = [.. path];
                    if (off) offGeoms.Add(ga.Index);
                }
            if (file.Is(node, "igSwitch"))
            {
                var bits = Bits(file, data, Ref(node, 8), le);
                var kids = Ref(node, 7) is { } list && file.Is(list, "igObjectList") ? scene.ObjectList(list).Where(x => file.Is(x, "igNode")).ToList() : Children(node).ToList();
                for (int k = 0; k < kids.Count; k++)
                {
                    string state = bits == null ? "sem máscara" : k < bits.Count ? (bits[k] ? "liga" : "desliga") : "fora da máscara";
                    var p = new List<string>(path) { $"sw#{node.Index} {NameOf(node.Index)}[{k}]={state}" };
                    Walk(kids[k], p, off || state == "desliga", seen);
                }
            }
            else foreach (var c in Children(node)) Walk(c, path, off, seen);
            seen.Remove(node.Index);
        }
        if (file.Slots[bg01] is IgbObject root) Walk(root, [], false, []);

        // Peças com nome / tipo (beHitLand*): campos crus e as geometrias de colisão alcançáveis.
        var hitInfo = new JsonArray();
        var hitOfGeom = new Dictionary<int, List<string>>();
        foreach (var hl in file.Slots.OfType<IgbObject>().Where(o => o.TypeName.StartsWith("beHitLand", StringComparison.Ordinal)))
        {
            var fields = new JsonObject();
            foreach (var f in hl.Fields) fields[$"@{f.Desc.Slot} {f.Desc.ShortName}"] = FieldText(file, f, le);
            var reach = scene.Reachable(hl);
            var geoms = reach.Where(i => file.Slots[i] is IgbObject o && file.Is(o, "igGeometryAttr")).ToList();
            var names = Ref(hl, 9) is { } nl && file.Is(nl, "igObjectList") ? scene.ObjectList(nl).Select(n => n.Fields.FirstOrDefault(v => v.Desc.ShortName == "String")?.Text ?? "").ToList() : [];
            string label = $"{hl.TypeName}#{hl.Index} {NameOf(hl.Index)}".Trim();
            foreach (var g in geoms) { if (!hitOfGeom.TryGetValue(g, out var l)) hitOfGeom[g] = l = []; l.Add(label); }
            hitInfo.Add(new JsonObject
            {
                ["objeto"] = label, ["campos"] = fields, ["nomes"] = new JsonArray(names.Select(n => (JsonNode?)n).ToArray()),
                ["geometrias"] = new JsonArray(geoms.Select(g => (JsonNode?)g).ToArray()),
            });
        }

        // Triângulos da colisão (os que o cliente carrega) e do cenário (todas as raízes BG02).
        var bg02Roots = scene.Parts.Select(p => p.SceneRoot).Distinct().Where(r => NameOf(r) == "BG02").ToHashSet();
        var col = new List<Tri>();
        var colParts = scene.Parts.Where(p => p.SceneRoot == bg01 && p.Triangles.Count > 0 && !markers.Contains(p.GeometryIndex)).ToList();
        foreach (var p in colParts) AddTris(col, p);
        var vis = new List<Tri>();
        foreach (var p in scene.Parts.Where(p => bg02Roots.Contains(p.SceneRoot) && p.Triangles.Count > 0)) AddTris(vis, p);
        var grid = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < vis.Count; i++)
        {
            var (mn, mx) = (vis[i].Min, vis[i].Max);
            for (int x = (int)MathF.Floor((mn.X - CoverDist) / Cell); x <= (int)MathF.Floor((mx.X + CoverDist) / Cell); x++)
                for (int z = (int)MathF.Floor((mn.Z - CoverDist) / Cell); z <= (int)MathF.Floor((mx.Z + CoverDist) / Cell); z++)
                {
                    if (!grid.TryGetValue((x, z), out var l)) grid[(x, z)] = l = [];
                    l.Add(i);
                }
        }
        bool Covered(Vector3 p)
        {
            if (!grid.TryGetValue(((int)MathF.Floor(p.X / Cell), (int)MathF.Floor(p.Z / Cell)), out var l)) return false;
            foreach (int i in l) if (DistSq(p, vis[i].A, vis[i].B, vis[i].C) <= CoverDist * CoverDist) return true;
            return false;
        }

        // Classifica.
        var uncovered = new List<(Tri T, float Area)>();
        var steps = new List<Tri>();
        var steep = new List<Tri>();
        foreach (var t in col)
        {
            var n = t.Normal;
            float ny = MathF.Abs(n.Y);
            if (t.Area < 0.5f) continue;
            if (ny >= 0.707f) continue; // chão
            if (ny >= 0.342f) { steep.Add(t); continue; }
            if (t.Max.Y - t.Min.Y <= StepMax) steps.Add(t);
            if (vis.Count == 0) continue;
            int total = 0, bad = 0;
            foreach (var s in Samples(t)) { total++; if (!Covered(s)) bad++; }
            if (total > 0 && bad * 2 > total) uncovered.Add((t, t.Area * bad / total));
        }

        JsonObject Info(int geom) => new()
        {
            ["geometria"] = geom,
            ["switch"] = new JsonArray((switchPath.GetValueOrDefault(geom) ?? []).Select(s => (JsonNode?)s).ToArray()),
            ["hitland"] = new JsonArray((hitOfGeom.GetValueOrDefault(geom) ?? []).Select(s => (JsonNode?)s).ToArray()),
        };
        var regions = Cluster(uncovered.Select(u => u.T).ToList(), 12f)
            .Select(c => (Tris: c, Area: c.Sum(t => uncovered.First(u => ReferenceEquals(u.T, t)).Area)))
            .OrderByDescending(c => c.Area).ToList();
        var regionJson = new JsonArray(regions.Select(r =>
        {
            var mn = r.Tris.Aggregate(new Vector3(float.MaxValue), (a, t) => Vector3.Min(a, t.Min));
            var mx = r.Tris.Aggregate(new Vector3(float.MinValue), (a, t) => Vector3.Max(a, t.Max));
            var c = (mn + mx) / 2;
            var normals = r.Tris.Select(t => t.Normal).Aggregate(Vector3.Zero, (a, n) => a + n);
            return (JsonNode?)new JsonObject
            {
                ["area"] = Math.Round(r.Area, 1), ["triangulos"] = r.Tris.Count,
                ["min"] = V(mn), ["max"] = V(mx), ["centro"] = V(c), ["normal_media"] = V(normals.LengthSquared() > 0 ? Vector3.Normalize(normals) : normals),
                ["pecas"] = new JsonArray(r.Tris.Select(t => t.Geometry).Distinct().Select(g => (JsonNode?)Info(g)).ToArray()),
            };
        }).ToArray());
        var offJson = new JsonArray(colParts.Where(p => offGeoms.Contains(p.GeometryIndex)).Select(p =>
        {
            var j = Info(p.GeometryIndex);
            var mn = p.Positions.Aggregate(new Vector3(float.MaxValue), (a, q) => Vector3.Min(a, new Vector3(q.X, q.Y, q.Z)));
            var mx = p.Positions.Aggregate(new Vector3(float.MinValue), (a, q) => Vector3.Max(a, new Vector3(q.X, q.Y, q.Z)));
            j["min"] = V(mn); j["max"] = V(mx); j["triangulos"] = p.Triangles.Count / 3;
            return (JsonNode?)j;
        }).ToArray());
        JsonArray Boxes(List<Tri> tris) => new(Cluster(tris, 6f).OrderByDescending(c => c.Sum(t => t.Area)).Take(40).Select(c =>
        {
            var mn = c.Aggregate(new Vector3(float.MaxValue), (a, t) => Vector3.Min(a, t.Min));
            var mx = c.Aggregate(new Vector3(float.MinValue), (a, t) => Vector3.Max(a, t.Max));
            return (JsonNode?)new JsonObject { ["min"] = V(mn), ["max"] = V(mx), ["triangulos"] = c.Count, ["geometrias"] = new JsonArray(c.Select(t => t.Geometry).Distinct().Select(g => (JsonNode?)g).ToArray()) };
        }).ToArray());

        float badArea = uncovered.Sum(u => u.Area);
        md.AppendLine($"| {stage} | {col.Count} | {badArea:0} | {regions.Count(r => r.Area >= 50)} | {offJson.Count} | {hitInfo.Count} | {Cluster(steps, 6f).Count} | {Cluster(steep, 6f).Count} |");
        Console.WriteLine($"{stage}: {col.Count} triângulos, {vis.Count} do cenário, paredes sem cenário {badArea:0} em {regions.Count} regiões, {offJson.Count} peças desligadas no switch, {hitInfo.Count} beHitLand*");
        return new JsonObject
        {
            ["fase"] = stage, ["principal"] = main, ["colisao_BG01"] = bg01, ["raizes_BG02"] = new JsonArray(bg02Roots.Select(r => (JsonNode?)r).ToArray()),
            ["triangulos_colisao"] = col.Count, ["triangulos_cenario"] = vis.Count, ["area_paredes_sem_cenario"] = Math.Round(badArea, 1),
            ["regioes_sem_cenario"] = regionJson, ["pecas_switch_desligado"] = offJson, ["hitland"] = hitInfo,
            ["degraus"] = Boxes(steps), ["rampas"] = Boxes(steep),
        };
    }

    static void AddTris(List<Tri> list, IgbMeshPart p)
    {
        for (int i = 0; i + 2 < p.Triangles.Count; i += 3)
        {
            Vector3 P(int k) { var q = p.Positions[p.Triangles[i + k]]; return new Vector3(q.X, q.Y, q.Z); }
            list.Add(new Tri(P(0), P(1), P(2), p.GeometryIndex));
        }
    }

    static IEnumerable<Vector3> Samples(Tri t)
    {
        float edge = MathF.Max((t.B - t.A).Length(), MathF.Max((t.C - t.B).Length(), (t.A - t.C).Length()));
        int n = Math.Clamp((int)MathF.Ceiling(edge / SampleStep), 1, 24);
        for (int i = 0; i <= n; i++)
            for (int j = 0; i + j <= n; j++)
            {
                float u = (i + 1f / 3) / (n + 1), v = (j + 1f / 3) / (n + 1);
                if (u + v > 1) continue;
                yield return t.A + (t.B - t.A) * u + (t.C - t.A) * v;
            }
    }

    // Agrupa triângulos cujas caixas se tocam (com folga).
    static List<List<Tri>> Cluster(List<Tri> tris, float gap)
    {
        var parent = Enumerable.Range(0, tris.Count).ToArray();
        int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        for (int i = 0; i < tris.Count; i++)
            for (int k = i + 1; k < tris.Count; k++)
            {
                var (a0, a1, b0, b1) = (tris[i].Min, tris[i].Max, tris[k].Min, tris[k].Max);
                if (a0.X - gap <= b1.X && b0.X - gap <= a1.X && a0.Y - gap <= b1.Y && b0.Y - gap <= a1.Y && a0.Z - gap <= b1.Z && b0.Z - gap <= a1.Z)
                    parent[Find(i)] = Find(k);
            }
        return Enumerable.Range(0, tris.Count).GroupBy(Find).Select(g => g.Select(i => tris[i]).ToList()).ToList();
    }

    static List<bool>? Bits(IgbFile file, byte[] data, IgbObject? mask, bool le)
    {
        if (mask == null) return null;
        var mr = mask.Fields.FirstOrDefault(v => v.Desc.ShortName == "MemoryRef");
        int mi = mr?.AsInt32(le) ?? -1;
        var mem = mi >= 0 && mi < file.Slots.Count && file.Slots[mi] is IgbMemoryBlock mb ? data.AsSpan((int)mb.FileOffset, mb.Size).ToArray() : [];
        int count = mask.Fields.FirstOrDefault(v => v.Desc.Slot == 5)?.AsInt32(le) ?? 0;
        var bits = new List<bool>();
        for (int i = 0; i < count && i / 32 * 4 + 4 <= mem.Length; i++)
        {
            uint w = le ? BinaryPrimitives.ReadUInt32LittleEndian(mem.AsSpan(i / 32 * 4)) : BinaryPrimitives.ReadUInt32BigEndian(mem.AsSpan(i / 32 * 4));
            bits.Add(((w >> (i % 32)) & 1) != 0);
        }
        return bits;
    }

    static JsonNode? FieldText(IgbFile file, IgbFieldValue f, bool le)
    {
        if (f.Text != null) return f.Text;
        if (f.Desc.ShortName is "ObjectRef" or "MemoryRef")
        {
            int i = f.AsInt32(le);
            return i >= 0 && i < file.Slots.Count ? $"#{i} {file.Slots[i].TypeName}" : "nulo";
        }
        if (f.Raw.Length == 4 && f.Desc.ShortName.Contains("Float", StringComparison.Ordinal))
            return Math.Round(le ? BinaryPrimitives.ReadSingleLittleEndian(f.Raw) : BinaryPrimitives.ReadSingleBigEndian(f.Raw), 4);
        if (f.Raw.Length == 4) return f.AsInt32(le);
        if (f.Raw.Length == 2) return le ? BinaryPrimitives.ReadInt16LittleEndian(f.Raw) : BinaryPrimitives.ReadInt16BigEndian(f.Raw);
        if (f.Raw.Length == 1) return f.Raw[0];
        return Convert.ToHexString(f.Raw.Length > 64 ? f.Raw[..64] : f.Raw);
    }

    static JsonArray V(Vector3 v) => new(Math.Round(v.X, 1), Math.Round(v.Y, 1), Math.Round(v.Z, 1));

    // Distância ao quadrado de um ponto a um triângulo (Ericson, Real-Time Collision Detection, 5.1.5).
    static float DistSq(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return ap.LengthSquared();
        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return bp.LengthSquared();
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) { float v = d1 / (d1 - d3); return (p - (a + ab * v)).LengthSquared(); }
        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return cp.LengthSquared();
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) { float w = d2 / (d2 - d6); return (p - (a + ac * w)).LengthSquared(); }
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) { float w = (d4 - d3) / (d4 - d3 + (d5 - d6)); return (p - (b + (c - b) * w)).LengthSquared(); }
        float denom = 1 / (va + vb + vc);
        float vv = vb * denom, ww = vc * denom;
        return (p - (a + ab * vv + ac * ww)).LengthSquared();
    }
}
