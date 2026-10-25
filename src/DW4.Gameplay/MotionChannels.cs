using System.Numerics;

namespace DW4.Gameplay;

/// <summary>
/// Canais de movimento do modelo, como o executor de beModelCtrlMOVE e beModelCtrlMOVEEX os monta (main.dol).
/// FATO (leitura estática):
///  · 0x8030A1F4 arma um canal: modo, máximo f1, aceleração f1 / (0,5·f2²) por quadro², tempo f3 (0 = sem fim). Os modos 4 e 5
///    começam com a velocidade +f1 e −f1.
///  · 0x8030A2D8 avança um canal por dt quadros (dt limitado a 3) e devolve o deslocamento v·dt. Modo 0 acelera até +máx e 1 até
///    −máx; 2 e 4 freiam de positivo até 0, 3 e 5 de negativo até 0, e o canal desliga ao chegar a 0.
///  · Com tempo, ao zerar ele chama 0x8030A254: o modo 0 passa ao 2 e o 1 ao 3, com o par de valores guardado (MOVEEX @7, @8);
///    os outros param.
///  · Canais 0..2 = deslocamento X, Y, Z no espaço local do modelo (0x80128DC8, pré-translação da matriz); canal 3 = giro Y em
///    graus (somado ao yaw com 0,017453292).
///  · MOVE (0x8030AA1C): "Stop" = Z modo 2, "Move" = Z modo 0, "Push" = Z modo 4, "Back" = Z modo 5, "Jump" = Y modo 4,
///    "Fall" = Y modo 1, todos sem tempo.
///  · MOVEEX (0x8030ABAC): o texto escolhe o canal e o modo. 1ª letra "T" (translação) ou outra (giro, +2 no canal); 2ª letra
///    X/Y/Z soma 0/1/2; 3ª letra "M" soma 1 ao modo; 4ª letra "A" = modo 0, "D" = 2, "T" = 4. Valores: @3 máx, @4, @5 tempo,
///    @6 o bool, @7 e @8 o máx e o @4 da fase de freio.
///  · Na troca de sequência (0x8030EA38) o giro (canal 3) para; os outros com o bool do MOVEEX passam pela 0x8030A254, e os sem
///    o bool continuam.
///  · MOVERL (0x8030ACA8): "JOINT" peça pede GETTRCDATA à peça e, com resposta, passa a copiar a matriz dela; outro texto
///    ("ACTOR" modelo junta) guarda os nomes e copia a matriz da junta (GETBONEMATRIX × GETMATRIX do modelo). Preso, os canais
///    não andam (0x8030CD30 pula 0x8030A448). O MOVE, MOVEEX ou MOVERL seguinte solta primeiro, com o yaw tirado da matriz atual
///    (0x8030A71C).
/// HIPÓTESES: a unidade é por quadro a 30 Hz (a mesma do pulo); o "R" de "RX" cairia no canal 2 pela conta, mas os dados só têm
/// RY.
/// </summary>
public sealed class MotionChannels
{
    public const int Count = 4;
    public const int RotateY = 3;

    public sealed class Channel
    {
        public bool Active;
        public int Mode;
        public float Max, Accel, Velocity, Timer;
        public bool EndOnSequence;
        public float NextMax, NextN;
    }

    readonly Channel[] _ch = Enumerable.Range(0, Count).Select(_ => new Channel()).ToArray();

    public Channel this[int i] => _ch[i];
    public bool AnyActive => Attached == null && _ch.Any(c => c.Active);
    /// <summary>MOVERL valendo ("JOINT" peça "" ou "ACTOR" modelo junta): quem desenha copia a matriz do alvo.</summary>
    public SeqCommand? Attached { get; private set; }

    /// <summary>0x8030A1F4.</summary>
    public void Set(int channel, int mode, float max, float n, float timer)
    {
        var c = _ch[channel];
        c.Mode = mode;
        c.Max = max;
        c.Accel = max / (n * n * 0.5f);
        c.Timer = timer;
        c.EndOnSequence = false;
        if (mode == 4) c.Velocity = max;
        else if (mode == 5) c.Velocity = -max;
        c.Active = true;
    }

    /// <summary>Aplica um MOVE ou MOVEEX; devolve false para outros comandos ou textos desconhecidos.</summary>
    public bool Apply(SeqCommand cmd)
    {
        if (cmd.Kind is "MOVE" or "MOVEEX" or "MOVERL") Attached = null;
        if (cmd.Kind == "MOVERL")
        {
            Attached = cmd;
            return true;
        }
        if (cmd.Kind == "MOVE")
        {
            (int ch, int mode) = cmd.S(0) switch
            {
                "Stop" => (2, 2), "Move" => (2, 0), "Push" => (2, 4), "Back" => (2, 5), "Jump" => (1, 4), "Fall" => (1, 1),
                _ => (-1, 0),
            };
            if (ch < 0) return false;
            Set(ch, mode, cmd.F(1), cmd.F(2), 0);
            return true;
        }
        if (cmd.Kind != "MOVEEX" || ParseEx(cmd.S(0)) is not var (channel, exMode) || channel is < 0 or >= Count) return false;
        Set(channel, exMode, cmd.F(1), cmd.F(2), cmd.F(3));
        var c = _ch[channel];
        c.EndOnSequence = cmd.Values.Count > 4 && cmd.Values[4] is true;
        c.NextMax = cmd.F(5);
        c.NextN = cmd.F(6);
        return true;
    }

    /// <summary>Canal e modo do texto do MOVEEX ("TYPACC" → canal 1, modo 0); null se curto demais.</summary>
    public static (int Channel, int Mode)? ParseEx(string s)
    {
        if (s.Length < 4) return null;
        int channel = (s[0] == 'T' ? 0 : 2) + s[1] - 'X';
        int mode = s[3] switch { 'A' => 0, 'D' => 2, 'T' => 4, _ => 0 };
        if (s[2] == 'M') mode++;
        return (channel, mode);
    }

    /// <summary>0x8030A254: fim do tempo (ou troca de sequência com o bool).</summary>
    void End(Channel c)
    {
        c.Timer = 0;
        if (c.Mode == 0) Set(Array.IndexOf(_ch, c), 2, c.NextMax, c.NextN, 0);
        else if (c.Mode == 1) Set(Array.IndexOf(_ch, c), 3, c.NextMax, c.NextN, 0);
        else { c.Active = false; c.Velocity = 0; }
        c.EndOnSequence = false;
    }

    /// <summary>0x8030EA38: começou outra sequência.</summary>
    public void SequenceChanged()
    {
        for (int i = 0; i < Count; i++)
        {
            var c = _ch[i];
            if (i == RotateY) { c.Active = false; c.Velocity = 0; }
            else if (c.EndOnSequence) End(c);
        }
    }

    public void Stop()
    {
        Attached = null;
        foreach (var c in _ch) { c.Active = false; c.Velocity = 0; c.Timer = 0; }
    }

    /// <summary>0x8030A2D8: avança um canal; devolve o deslocamento do passo.</summary>
    public float Step(int channel, float frames)
    {
        var c = _ch[channel];
        if (!c.Active || Attached != null) return 0;
        if (c.Timer != 0)
        {
            float left = c.Timer - frames;
            if (left < 0)
            {
                End(c);
                if (!c.Active) return 0;
            }
            else c.Timer = left;
        }
        frames = MathF.Min(frames, 3);
        float a = frames * c.Accel, v = c.Velocity;
        switch (c.Mode)
        {
            case 0: v = MathF.Min(v + a, c.Max); break;
            case 1: v = MathF.Max(v - a, -c.Max); break;
            case 2 or 4:
                v -= a;
                if (v < 0) { v = 0; c.Active = false; }
                break;
            case 3 or 5:
                v += a;
                if (v > 0) { v = 0; c.Active = false; }
                break;
        }
        c.Velocity = v;
        return v * frames;
    }

    /// <summary>Avança todos: deslocamento local (X, Y, Z) e giro Y em graus.</summary>
    public (Vector3 Local, float YawDegrees) Step(float frames) =>
        (new Vector3(Step(0, frames), Step(1, frames), Step(2, frames)), Step(RotateY, frames));
}
