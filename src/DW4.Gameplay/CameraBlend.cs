using System.Numerics;

namespace DW4.Gameplay;

/// <summary>
/// Passagem suave entre duas poses de câmera (DECISÃO DO PORT, docs/decisoes-port-pc.md): no original a troca de volume é um
/// corte seco (HIPÓTESE em <see cref="CameraZones"/>); aqui as poses de cada volume continuam as do jogo e só o caminho entre
/// elas é interpolado. Pivô e recuo são guardados relativos ao alvo, então a câmera segue o jogador durante a passagem. O recuo
/// (câmera − pivô) gira em arco em volta do pivô, com o comprimento interpolado: a câmera faz uma leve rotação em vez de cortar
/// ou atravessar em linha reta. Uma troca no meio da passagem parte da pose mostrada naquele quadro.
/// </summary>
public sealed class CameraBlend
{
    /// <summary>Duração da passagem, em segundos (0 ou menos = corte seco).</summary>
    public float Duration { get; set; } = PortConfig.DefaultCameraBlend;
    /// <summary>Se o alvo pula mais que isso num quadro (teleporte, troca de entrada), a câmera corta.</summary>
    public float SnapDistance { get; set; } = 300f;

    bool _has;
    int _key;
    float _t = 1;
    Vector3 _lastTarget;
    Vector3 _fromPivot, _fromOffset, _curPivot, _curOffset;

    /// <summary>Esquece a pose atual: o próximo <see cref="Update"/> corta direto (início da fase, fim de uma demo).</summary>
    public void Reset() => _has = false;

    /// <summary>True enquanto a passagem não terminou.</summary>
    public bool Blending => _has && _t < 1;

    /// <summary>
    /// Pose a mostrar neste quadro. <paramref name="key"/> identifica a pose de destino (volume e conjunto de câmera): quando muda,
    /// começa uma passagem a partir da pose mostrada até agora.
    /// </summary>
    public (Vector3 Position, Vector3 LookAt) Update(int key, Vector3 position, Vector3 lookAt, Vector3 target, float dt)
    {
        var toPivot = lookAt - target;
        var toOffset = position - lookAt;
        if (!_has || Duration <= 0 || Vector3.Distance(target, _lastTarget) > SnapDistance)
        {
            _has = true;
            _key = key;
            _t = 1;
            _curPivot = toPivot;
            _curOffset = toOffset;
        }
        else
        {
            if (key != _key)
            {
                _key = key;
                _fromPivot = _curPivot;
                _fromOffset = _curOffset;
                _t = 0;
            }
            if (_t < 1)
            {
                _t = Math.Min(1, _t + dt / Duration);
                float s = Ease(_t);
                _curPivot = Vector3.Lerp(_fromPivot, toPivot, s);
                _curOffset = Arc(_fromOffset, toOffset, s);
            }
            else
            {
                _curPivot = toPivot;
                _curOffset = toOffset;
            }
        }
        _lastTarget = target;
        var look = target + _curPivot;
        return (look + _curOffset, look);
    }

    /// <summary>Começa e termina devagar (smoothstep).</summary>
    public static float Ease(float t) => t * t * (3 - 2 * t);

    /// <summary>Gira <paramref name="a"/> até <paramref name="b"/> pelo menor arco, com o comprimento interpolado.</summary>
    public static Vector3 Arc(Vector3 a, Vector3 b, float s)
    {
        float la = a.Length(), lb = b.Length();
        if (la < 1e-4f || lb < 1e-4f) return Vector3.Lerp(a, b, s);
        var da = a / la;
        var db = b / lb;
        float dot = Math.Clamp(Vector3.Dot(da, db), -1f, 1f);
        float angle = MathF.Acos(dot);
        float len = la + (lb - la) * s;
        if (angle < 1e-3f) return Vector3.Normalize(Vector3.Lerp(da, db, s)) * len;
        var axis = Vector3.Cross(da, db);
        if (axis.LengthSquared() < 1e-8f)
        {
            // Opostos: gira em volta do eixo vertical (ou de X, se o recuo for vertical).
            axis = Vector3.UnitY - da * da.Y;
            if (axis.LengthSquared() < 1e-8f) axis = Vector3.UnitX;
        }
        var q = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), angle * s);
        return Vector3.Transform(da, q) * len;
    }
}
