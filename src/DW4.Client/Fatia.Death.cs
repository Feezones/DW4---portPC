using System;
using Godot;

namespace DW4.Client;

/// <summary>
/// Queda do jogador (HP 0). FATO:
///  · o SQ_DDMG (golpe com o HP em 0) e o SQ_DEAD (veneno, 0x803AD204) do jogador terminam em JUMP "SQ_ERAS";
///  · DEADRESTART da GAMERAM (0x80371B3C) põe HP = MHP e MP = MMP em cada jogador com ISUSE;
///  · textos da Central Area: "If you're destroyed out in the field, you'll lose all the BITS you've collected" e "That's why it's
///    best to deposit your BITS in the bank".
/// DESCONHECIDO: o código que reage a todos os jogadores caídos e para onde ele manda (não achado no main.dol).
/// DECISÃO DO PORT (no lugar do "F5 reinicia" da fatia): depois da animação de queda (≈ 3 s), perde os BITS carregados se a fase
/// é campo ou masmorra (fora das fases stagML*), recupera HP e MP como o DEADRESTART e volta ao lobby stagML11, entrada 3.
/// </summary>
public partial class Fatia
{
    const float DeathDelay = 3f;
    float _deathTimer = -1;

    void TickDeath(float dt)
    {
        if (_pf == null || _leaving) return;
        if (_hp > 0) { _deathTimer = -1; return; }
        if (_deathTimer < 0) { _deathTimer = 0; return; }
        _deathTimer += dt;
        if (_deathTimer < DeathDelay) return;
        _deathTimer = -1;
        bool field = !_stageName.StartsWith("stagML", StringComparison.OrdinalIgnoreCase);
        int lost = field ? _inventory.Bit : 0;
        if (field) _inventory.Bit = 0;
        _hp = _pf.MaxHp;
        _mp = _pf.MaxMp;
        ScriptLog($"caiu: {(field ? $"perdeu {lost} BIT; " : "")}volta ao lobby");
        _nextEntry = LobbyEntry;
        NextStage(LobbyStage);
    }
}
