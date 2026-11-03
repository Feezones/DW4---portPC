using System;
using Godot;

namespace DW4.Client;

/// <summary>
/// Queda do jogador (HP 0). FATO:
///  · o SQ_DDMG (golpe com o HP em 0) e o SQ_DEAD (veneno, 0x803AD204) do jogador terminam em JUMP "SQ_ERAS";
///  · DEADRESTART da GAMERAM (0x80371B3C) põe HP = MHP e MP = MMP em cada jogador com ISUSE;
///  · textos da Central Area: "If you're destroyed out in the field, you'll lose all the BITS you've collected" e "That's why it's
///    best to deposit your BITS in the bank".
/// FATO (chk_i_timenm0.lua, tempo da missão esgotado): todos em SQ_DEAD → i_fadewn01 SQ_GAMEOVER → some a música QS_HURRY →
/// toca QS_DONOT → i_messwn com a mensagem → PLAYERPOINT SETGENERATEPOS I<0> → NEXTSTAGE "stagML11" (lobby, entrada 0).
/// DESCONHECIDO: o código nativo que reage à queda fora das missões (não achado no main.dol; o DEADRESTART não é mandado por
/// nenhum texto do código nem dos scripts).
/// DECISÃO DO PORT (no lugar do "F5 reinicia" da fatia): depois da animação de queda (≈ 3 s), perde os BITS carregados se a fase
/// é campo ou masmorra (fora das fases stagML*), recupera HP e MP como o DEADRESTART e segue o fluxo do fim de missão do
/// próprio jogo: escurece com SQ_GAMEOVER, toca QS_DONOT e volta ao lobby stagML11, entrada 0 (a mensagem não foi identificada).
/// </summary>
public partial class Fatia
{
    const float DeathDelay = 3f, GameOverDelay = 2f;
    const int DeathEntry = 0;
    bool _gameOver;
    float _deathTimer = -1;

    void TickDeath(float dt)
    {
        if (_pf == null || _leaving) return;
        if (_hp > 0) { _deathTimer = -1; _gameOver = false; return; }
        if (_deathTimer < 0) { _deathTimer = 0; return; }
        _deathTimer += dt;
        if (_deathTimer < DeathDelay) return;
        if (!_gameOver)
        {
            _gameOver = true;
            StartFade("SQ_GAMEOVER");
            PlayMusic("QS_DONOT");
            return;
        }
        if (_deathTimer < DeathDelay + GameOverDelay) return;
        _deathTimer = -1;
        _gameOver = false;
        bool field = !_stageName.StartsWith("stagML", StringComparison.OrdinalIgnoreCase);
        int lost = field ? _inventory.Bit : 0;
        if (field) _inventory.Bit = 0;
        _hp = _pf.MaxHp;
        _mp = _pf.MaxMp;
        ScriptLog($"caiu: {(field ? $"perdeu {lost} BIT; " : "")}volta ao lobby");
        _nextEntry = DeathEntry;
        NextStage(LobbyStage);
    }
}
