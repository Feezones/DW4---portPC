using System.Numerics;
using System.Text.Json;

namespace DW4.Gameplay;

/// <summary>Uma escolha de IA (beModelCtrlAIData): sequência, ângulo em rad, valor (quadros) e limiar do sorteio.</summary>
public sealed record AiChoice(string Sequence, float Angle, float Value, int Threshold);

/// <summary>
/// Mapa de IA (beModelCtrlAIMap). FATO: nome, três floats crescentes que são quadrados exatos (36², 45², 150²…),
/// um float pequeno (0,0436 = 2,5°), quatro listas de escolhas e as strings "NEAR"/"". Leitura em
/// docs/gameplay/fase3-levantamento.md. HIPÓTESE (a medir no jogo):
///  · os três primeiros são distâncias ao alvo ao quadrado que separam quatro faixas, uma lista por faixa;
///  · o quarto é o giro máximo por quadro (rad);
///  · Int@5 é um limiar acumulado em 16: sorteia-se 0..15 e vale a primeira escolha com limiar maior que o sorteio.
/// </summary>
public sealed record AiMap(string Name, float[] RangesSquared, float TurnPerFrame, IReadOnlyList<IReadOnlyList<AiChoice>> Bands)
{
    public int Band(float distanceSquared)
    {
        for (int i = 0; i < RangesSquared.Length; i++)
            if (distanceSquared < RangesSquared[i]) return i;
        return RangesSquared.Length;
    }

    public AiChoice? Pick(float distanceSquared, int roll)
    {
        int band = Band(distanceSquared);
        if (band >= Bands.Count || Bands[band].Count == 0) return null;
        var list = Bands[band];
        return list.FirstOrDefault(c => c.Threshold > roll) ?? list[^1];
    }

    /// <summary>Lê os mapas ("aimaps") do JSON gravado por <c>dw4 igb-seq … --ator=</c>.</summary>
    public static List<AiMap> LoadAll(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<AiMap>();
        if (!doc.RootElement.TryGetProperty("aimaps", out var maps)) return result;
        foreach (var m in maps.EnumerateArray())
        {
            var values = m.GetProperty("values").EnumerateArray().Select(x => x.GetSingle()).ToArray();
            var bands = m.GetProperty("lists").EnumerateArray().Select(l => (IReadOnlyList<AiChoice>)l.EnumerateArray().Select(c =>
            {
                var v = c.EnumerateArray().ToArray();
                return new AiChoice(v[0].GetString() ?? "", v[1].GetSingle(), v[2].GetSingle(), v[3].GetInt32());
            }).ToList()).ToList();
            result.Add(new AiMap(m.GetProperty("name").GetString() ?? "", values.Take(3).ToArray(), values.Length > 3 ? values[3] : 0, bands));
        }
        return result;
    }
}

/// <summary>
/// Cérebro de um inimigo a partir dos mapas de IA do jogo, sem renderização. Escolhas e valores são FATO (dados);
/// a execução é HIPÓTESE (docs/gameplay/fase3-levantamento.md):
///  · sem ordem em andamento, sorteia uma escolha no mapa atual pela distância ao alvo;
///  · SQ_WALK: anda por Value quadros, virando no máximo TurnPerFrame por quadro para a direção do alvo + Angle;
///  · SQ_WAIT e SQ_SILENT: fica parado por Value quadros;
///  · SQ_CHMOD0/1: troca de mapa (o AIMAP da sequência diz qual) e escolhe de novo no quadro seguinte;
///  · outras (SQ_ATCK1, SQ_FIRE…): sequência do jogo executada por fora; o cérebro espera <see cref="Release"/>.
/// </summary>
public sealed class EnemyBrain
{
    public const float FramesPerSecond = 30f;
    readonly IReadOnlyList<AiMap> _maps;
    readonly IReadOnlyDictionary<string, List<SeqCommand>> _sequences;
    readonly Func<int> _roll;
    float _framesLeft;
    bool _repick;

    public EnemyBrain(IReadOnlyList<AiMap> maps, IReadOnlyDictionary<string, List<SeqCommand>> sequences, Func<int> roll16)
    {
        if (maps.Count == 0) throw new ArgumentException("nenhum mapa de IA", nameof(maps));
        _maps = maps;
        _sequences = sequences;
        _roll = roll16;
        Map = maps[0];
    }

    public AiMap Map { get; private set; }
    public AiChoice? Current { get; private set; }
    /// <summary>Sequência do jogo que deve ser executada por fora (ataque etc.); fica até <see cref="Release"/>.</summary>
    public string? External { get; private set; }
    public float Yaw { get; private set; }
    public bool Walking => Current?.Sequence == "SQ_WALK";

    public void Release() { External = null; Current = null; }

    /// <summary>Interrompe a IA com uma sequência do jogo (ex.: SQ_SDMG ao levar golpe), até <see cref="Release"/>.</summary>
    public void Force(string sequence) { External = sequence; Current = null; }

    /// <summary>AIMAP de uma sequência forçada (ex.: SQ_PHASE2 do f_apocaly troca para MAP01).</summary>
    public void SetMap(string name) { Map = _maps.FirstOrDefault(m => m.Name == name) ?? Map; Current = null; }

    /// <summary>Avança a IA. Yaw segue a convenção de <see cref="PadMovement.Facing"/> (atan2(x, z)).</summary>
    public void Tick(float dt, Vector3 self, Vector3 target, float yaw)
    {
        Yaw = yaw;
        if (External != null) return;
        if (_repick) { _repick = false; Current = null; }
        var to = target - self;
        float toYaw = MathF.Atan2(to.X, to.Z);
        if (Current == null)
        {
            Current = Map.Pick(to.X * to.X + to.Z * to.Z, _roll());
            if (Current == null) return;
            _framesLeft = Current.Value;
            switch (Current.Sequence)
            {
                case "SQ_WALK" or "SQ_WAIT" or "SQ_SILENT":
                    break;
                case var s when s.StartsWith("SQ_CHMOD", StringComparison.Ordinal):
                    var name = _sequences.TryGetValue(s, out var cmds) ? cmds.FirstOrDefault(c => c.Kind == "AIMAP")?.S(0) : null;
                    Map = _maps.FirstOrDefault(m => m.Name == name) ?? Map;
                    Current = null;
                    return;
                default:
                    External = Current.Sequence;
                    Yaw = toYaw; // HIPÓTESE: o golpe sai virado para o alvo
                    return;
            }
        }
        float frames = dt * FramesPerSecond;
        if (Current.Sequence == "SQ_WALK") Yaw = TurnTowards(Yaw, toYaw + Current.Angle, Map.TurnPerFrame * frames);
        // HIPÓTESE (f_belvam MAP07, lista de longe: "SQ_WALK" 0 0 16): valor 0 = segue nesta escolha só neste quadro e decide de novo
        // no próximo (anda até mudar a faixa de distância). Sem isso o chefe longe nunca andava.
        if (Current.Value <= 0) { _repick = true; return; }
        _framesLeft -= frames;
        if (_framesLeft <= 0) Current = null;
    }

    static float TurnTowards(float from, float to, float maxStep)
    {
        float d = MathF.IEEERemainder(to - from, 2 * MathF.PI);
        return from + Math.Clamp(d, -maxStep, maxStep);
    }
}
