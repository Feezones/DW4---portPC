using System.IO;
using System.Linq;
using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>
/// Menu de status (TAB), beNDMWStatus no original (código nativo). Desde 2026-11-02 a tela é o <see cref="StatusScreen"/>, com
/// layout próprio no estilo do jogo e as subtelas Equipment / Folder / Techniques (levantamento: docs/gameplay/fase4-menu-status.md
/// e docs/gameplay/menu-status-tab.md). A montagem antiga pelos atores i_st* (SubViewport 640 × 480) saiu: ficava torta e só tinha
/// a tela base.
/// HIPÓTESES mantidas: o botão do status abre e fecha; a fase fica parada com o menu aberto.
/// </summary>
public partial class Fatia
{
    bool _menuOpen;
    StatusScreen? _statusScreen;
    CanvasLayer? _menuLayer;
    Texture2D? _faces;
    bool _facesTried;

    // Rostos do menu (charaicon.png do i_stdigiface, exportado em <fatia>\menu com o HUD).
    Texture2D? Faces()
    {
        if (_facesTried) return _faces;
        _facesTried = true;
        foreach (var dir in new[] { Path.Combine(_dir, "menu"), Path.Combine(_common, "menu"), Path.Combine(_common, "hud") })
        {
            var file = GameFiles.DirExists(dir) ? GameFiles.EnumerateFiles(dir, "*charaicon.png").FirstOrDefault() : null;
            if (file != null && Image.LoadFromFile(GameFiles.GodotPath(file)) is { } img) return _faces = ImageTexture.CreateFromImage(img);
        }
        return null;
    }

    /// <summary>Com o menu aberto o StatusScreen lê as teclas antes (_Input); aqui só abre e segura o resto.</summary>
    bool MenuInput(InputEvent e)
    {
        if (_menuOpen) return true;
        if (!PortInput.Pressed(e, "status") || ShopOpen || SubWindowOpen || _db == null) return false;
        OpenMenu();
        return true;
    }

    void OpenMenu()
    {
        if (_db == null || _menuOpen) return;
        _menuLayer ??= new CanvasLayer { Layer = 5 };
        if (!_menuLayer.IsInsideTree()) AddChild(_menuLayer);
        _menuOpen = true;
        _statusScreen = new StatusScreen(new StatusContext
        {
            Db = _db,
            Inventory = () => _inventory,
            Equipment = () => _equip,
            SetEquipment = eq =>
            {
                _equip = eq;
                Ram.Players[0].Equipment = eq;
                Recalc();
                RefreshWeapon();
                _hp = System.Math.Min(_hp, _pf!.MaxHp);
                _mp = System.Math.Min(_mp, _pf.MaxMp);
            },
            Preview = eq =>
            {
                var up = Ram.Players[0].Forms.Current == _shinkaRow && _shinkaRow >= 0 ? Ram.Players[0].Forms[_shinkaRow].UpCount : null;
                return _db.Player(_shinkaRow, _level, eq, _skill, up);
            },
            Player = () => _pf,
            Numbers = () => (_level, _hp, _mp, _exp),
            Name = PlayerName,
            ShinkaRow = _shinkaRow,
            Skill = () => _skill,
            Forms = Ram.Players[0].Forms.Current >= 0 ? Ram.Players[0].Forms : null,
            TextByKey = k => TextKey(k, "") is { Length: > 0 } t ? t : null,
            Font = GameFont(),
            Faces = Faces(),
            Log = ScriptLog,
        });
        _statusScreen.Closed += () => { _menuOpen = false; _statusScreen = null; };
        _menuLayer.AddChild(_statusScreen);
    }

    /// <summary>Com o menu aberto a fase fica parada (HIPÓTESE): só o menu anda. true = pular o resto do quadro.</summary>
    bool TickMenu(float dt) => _menuOpen;
}
