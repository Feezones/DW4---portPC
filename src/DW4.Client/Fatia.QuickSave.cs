using Godot;

namespace DW4.Client;

/// <summary>
/// Salvar rápido e acelerar cenas (DECISÃO DO PORT; o jogo original grava só nos pontos de save e não acelera cenas).
///  · Salvar rápido (ação "salvar_rapido", F5 no teclado, sem padrão no controle): grava a vaga atual na hora, no mesmo arquivo do
///    save automático (fase e entrada atuais, como o save ao trocar de fase), e avisa na tela. Recusado em demos, cenas, menus,
///    janelas, fases de menu, caído ou sem vaga.
///  · Acelerar cenas (ação "acelerar", Shift / RT, segurar): durante cenas de demonstração (câmera de demo ou controle travado
///    pelo script) o tempo do jogo corre 2x/3x/4x (Engine.TimeScale, escolhido nas Configurações). Os scripts rodam inteiros, só
///    mais rápido; o som não muda.
/// </summary>
public partial class Fatia
{
    Label? _toast;
    double _toastLeft;

    bool InCutscene => _cameraDemo != null || (!_padEnabled && !SubWindowOpen && !_menuOpen && !ShopOpen) || !_playerCtrl;

    void QuickSave()
    {
        string? why = _demo || InCutscene ? "Não dá para salvar durante uma cena."
            : _menuOpen || ShopOpen || SubWindowOpen || ChatOpen ? "Feche a janela para salvar."
            : IsMenuStage(_stageName) ? "Não dá para salvar aqui."
            : _stageName.Equals(DW4.Script.Save.ResumePoint.Tutorial, System.StringComparison.OrdinalIgnoreCase) ? "O tutorial não grava (como no original)."
            : _pf != null && _hp <= 0 ? "Não dá para salvar caído."
            : Session.Character == null ? "Sem vaga: abra pelo menu inicial para salvar."
            : _leaving ? "" : null;
        if (why != null) { if (why.Length > 0) Toast(why); return; }
        SaveProgress();
        Toast($"Jogo salvo ({Session.Character!.Name}).");
    }

    void Toast(string text, double seconds = 2.5)
    {
        if (_toast == null)
        {
            var layer = new CanvasLayer { Layer = 25 };
            _toast = new Label { AnchorLeft = 0.5f, AnchorRight = 0.5f, OffsetLeft = -300, OffsetRight = 300, OffsetTop = 60, HorizontalAlignment = HorizontalAlignment.Center };
            _toast.AddThemeFontSizeOverride("font_size", 26);
            _toast.AddThemeConstantOverride("outline_size", 8);
            _toast.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
            layer.AddChild(_toast);
            AddChild(layer);
            UseGameFont(_toast);
        }
        _toast.Text = text;
        _toast.Visible = true;
        _toast.Modulate = Colors.White;
        _toastLeft = seconds;
    }

    // Chamado por quadro: some com o aviso e acerta a velocidade do tempo.
    void TickQuickSaveAndSpeed(double delta)
    {
        if (_toast != null && _toastLeft > 0)
        {
            _toastLeft -= delta / Engine.TimeScale;
            _toast.Modulate = new Color(1, 1, 1, (float)System.Math.Clamp(_toastLeft / 0.5, 0, 1));
            if (_toastLeft <= 0) _toast.Visible = false;
        }
        bool fast = _hasFocus && !ChatOpen && InCutscene && PortInput.Down("acelerar");
        double scale = fast ? PortSettings.Current.FastForward : 1;
        if (Engine.TimeScale != scale) Engine.TimeScale = scale;
    }

    public override void _ExitTree() => Engine.TimeScale = 1;
}
