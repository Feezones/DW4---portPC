using System;
using System.Collections.Generic;
using System.Linq;
using DW4.Net;
using Godot;

namespace DW4.Client;

/// <summary>
/// Outros jogadores na fase (online, ADR 0003): o modelo da forma de cada um, com a placa "Personagem  Lv N", andando para a
/// posição recebida (interpolação curta) e tocando a animação que ele toca. Sem colisão; o combate em grupo está em Fatia.Coop.cs.
/// </summary>
public partial class Fatia
{
    sealed class Avatar
    {
        public required Node3D Root;
        public Node3D? Model;
        public AnimationPlayer? Anim;
        public Nameplate? Plate;
        public string ModelName = "";
        public string? WeaponName; // null = ainda sem arma posta
        public BoneAttachment3D? WeaponRight, WeaponLeft;
        public WorldObject? Chip; // cristal do colega caído (Fatia.Spectator.cs)
        public double DeadSince = -1;
        public bool ReviveSent;
    }

    readonly Dictionary<int, Avatar> _avatars = [];

    void SetupOnline()
    {
        Online.Notice += OnlineNotice;
        Online.Result += OnInteractResult;
        Online.Prompt += OnSocialPrompt;
        Online.TradeIn += OnTradeView;
        SetupGuild();
        Online.Coop += OnCoop;
        Online.Coop += OnSpectatorCoop;
        Online.KickedOut += OnKicked;
        Session.SaveFailed += OnSaveFailed;
        // Com vaga, fechar a janela espera a gravação no servidor (CloseRequested).
        GetTree().AutoAcceptQuit = !Session.Active;
        TreeExiting += () =>
        {
            Online.Notice -= OnlineNotice; Online.Result -= OnInteractResult; Online.Prompt -= OnSocialPrompt; Online.TradeIn -= OnTradeView; Online.Coop -= OnCoop; Online.Coop -= OnSpectatorCoop;
            Online.KickedOut -= OnKicked; Session.SaveFailed -= OnSaveFailed;
            GetTree().AutoAcceptQuit = true;
            Ram.Ninzu = 1;
        };
        Online.Connect(PlayerName, PlayerModel, _level);
    }

    void OnlineNotice(string text) => Session.SystemMessage(text);

    PlayerState MyState()
    {
        var p = _player.GlobalPosition;
        return new PlayerState(_stageName, MathF.Round(p.X, 2), MathF.Round(p.Y, 2), MathF.Round(p.Z, 2), MathF.Round(_move.Facing, 3),
            _anim?.CurrentAnimation ?? "", PlayerModel, _level, WeaponModel ?? "", Math.Max(0, _hp), _pf?.MaxHp ?? 0, Math.Max(0, _mp), _pf?.MaxMp ?? 0);
    }

    void TickOnline(float dt)
    {
        TickCoop(dt); // o Online.Poll é feito no começo do _PhysicsProcess
        TickPartyHud();
        TickSelfIcon();
        TickReviverDebug(dt);
        TickInteractHint();
        foreach (var id in _avatars.Keys.ToList())
            if (!Online.Remotes.TryGetValue(id, out var r) || !r.State.Stage.Equals(_stageName, StringComparison.OrdinalIgnoreCase))
            {
                RemoveMateChip(_avatars[id]);
                _avatars[id].Root.QueueFree();
                _avatars.Remove(id);
            }
        foreach (var r in Online.Remotes.Values)
        {
            if (!r.State.Stage.Equals(_stageName, StringComparison.OrdinalIgnoreCase)) continue;
            var target = new Vector3(r.State.X, r.State.Y, r.State.Z);
            if (!_avatars.TryGetValue(r.Id, out var a))
            {
                a = new Avatar { Root = new Node3D { Name = "jogador_" + r.Id, Position = target } };
                AddChild(a.Root);
                _avatars[r.Id] = a;
            }
            if (a.ModelName != r.State.Model) SetAvatarModel(a, r.State.Model);
            if (a.WeaponName != r.State.Weapon) SetAvatarWeapon(a, r.State.Weapon);
            // Mais de 30 unidades de distância (teleporte, troca de entrada): pula direto.
            a.Root.Position = a.Root.Position.DistanceTo(target) > 30 ? target : a.Root.Position.Lerp(target, Math.Min(1f, dt * 12f));
            if (a.Model != null) a.Model.Rotation = new Vector3(0, r.State.Facing, 0);
            if (a.Anim != null && r.State.Anim.Length > 0 && a.Anim.CurrentAnimation != r.State.Anim && a.Anim.HasAnimation(r.State.Anim))
                a.Anim.Play(r.State.Anim, 0.13f);
            a.Plate?.Set(r.Character, r.State.Level);
            a.Plate?.SetGuild(r.Guild);
            UpdateMateChip(a, r);
        }
    }

    void SetAvatarModel(Avatar a, string model)
    {
        a.Model?.QueueFree();
        (a.Model, a.Anim, a.Plate, a.WeaponRight, a.WeaponLeft) = (null, null, null, null, null);
        a.ModelName = model;
        a.WeaponName = null; // o modelo novo vem sem arma; o próximo quadro põe a arma de novo
        // Só formas que o cliente tem (o nome vem do outro jogador: nada de caminho).
        if (model.Length == 0 || model.IndexOfAny(['/', '\\', '.']) >= 0) return;
        string folder = model == "p_girumon" && GameFiles.DirExists(System.IO.Path.Combine(_common, "guilmon"))
            ? "guilmon" : System.IO.Path.Combine("formas", model);
        if (Load(folder, "personagem.gltf") is not { } m) return;
        a.Root.AddChild(m);
        a.Model = m;
        a.Anim = FindAll<AnimationPlayer>(m).FirstOrDefault();
        if (a.Anim != null) TrimWrapSamples(a.Anim); // como no próprio jogador (Fatia.cs)
        CsSkin.TryAttach(m, model, a.Anim); // teste local, só com DW4_CS_MODELOS
        a.Plate = Nameplate.Attach(m);
    }

    // Arma na mão do outro jogador: o mesmo modelo e os mesmos ossos da arma do jogador (Fatia.Weapon.cs).
    void SetAvatarWeapon(Avatar a, string weapon)
    {
        a.WeaponRight?.QueueFree();
        a.WeaponLeft?.QueueFree();
        (a.WeaponRight, a.WeaponLeft) = (null, null);
        a.WeaponName = weapon;
        if (a.Model == null || weapon.IndexOfAny(['/', '\\', '.']) >= 0) return;
        // Sem arma equipada ("") fica a arma padrão, como na mão do próprio jogador (RefreshWeapon).
        (a.WeaponRight, a.WeaponLeft) = AttachWeapon(a.Model, weapon.Length > 0 ? weapon : null);
    }
}
