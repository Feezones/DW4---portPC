namespace DW4.Formats.Sofdec;

/// <summary>Quadro decodificado, YCbCr 4:2:0 com as dimensões arredondadas para macroblocos (16 px).</summary>
public sealed class Mpeg1Frame
{
    public int Width { get; }
    public int Height { get; }
    /// <summary>Largura e altura das linhas guardadas (múltiplos de 16 na luma).</summary>
    public int Stride { get; }
    public byte[] Y { get; }
    public byte[] Cb { get; }
    public byte[] Cr { get; }
    /// <summary>Posição na ordem de exibição (0, 1, 2…).</summary>
    public int Index { get; internal set; }
    /// <summary>Tipo da imagem: 'I', 'P' ou 'B'.</summary>
    public char Type { get; internal set; }

    internal Mpeg1Frame(int width, int height, int mbWidth, int mbHeight)
    {
        Width = width;
        Height = height;
        Stride = mbWidth * 16;
        Y = new byte[Stride * mbHeight * 16];
        Cb = new byte[Y.Length / 4];
        Cr = new byte[Y.Length / 4];
        Array.Fill(Y, (byte)16);
        Array.Fill(Cb, (byte)128);
        Array.Fill(Cr, (byte)128);
    }

    /// <summary>RGBA 8 bits (Width × Height × 4), BT.601 de faixa limitada, como os vídeos MPEG-1.</summary>
    public void ToRgba(byte[] dst)
    {
        int cs = Stride / 2;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int yy = (Y[y * Stride + x] - 16) * 76309; // 1,164 × 2^16
                int cb = Cb[(y >> 1) * cs + (x >> 1)] - 128, cr = Cr[(y >> 1) * cs + (x >> 1)] - 128;
                int o = (y * Width + x) * 4;
                dst[o] = Clamp((yy + 104597 * cr + 32768) >> 16);
                dst[o + 1] = Clamp((yy - 25675 * cb - 53279 * cr + 32768) >> 16);
                dst[o + 2] = Clamp((yy + 132201 * cb + 32768) >> 16);
                dst[o + 3] = 255;
            }
    }

    static byte Clamp(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
}

/// <summary>
/// Decodificador de vídeo MPEG-1 (ISO/IEC 11172-2), escrito para a abertura Sofdec do DW4 (640 × 480, 29,97 fps): imagens I, P e
/// B, VLC das tabelas do anexo B, quantização com as matrizes do cabeçalho de sequência, IDCT 8 × 8 e compensação de movimento de
/// meio pixel. Entra o fluxo elementar inteiro (o 0xE0 do <see cref="MpegPs"/>); sai um quadro por vez na ordem de exibição.
/// A organização (leitor de bits, árvores de VLC, laço de fatias e macroblocos, troca das referências) segue a do pl_mpeg
/// (Dominic Szablewski, licença MIT, https://github.com/phoboslab/pl_mpeg), usado só como referência de leitura; as tabelas e
/// as regras vêm da norma. Imagens D e as extensões do MPEG-2 não são tratadas (o DW4 não usa).
/// </summary>
public sealed class Mpeg1Video
{
    public static readonly double[] FrameRates = [0, 24000 / 1001.0, 24, 25, 30000 / 1001.0, 30, 50, 60000 / 1001.0, 60];

    static readonly int[] ZigZag =
    [
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
    ];

    // Matriz intra padrão, na ordem natural (linha por linha).
    static readonly byte[] DefaultIntra =
    [
        8, 16, 19, 22, 26, 27, 29, 34, 16, 16, 22, 24, 27, 29, 34, 37, 19, 22, 26, 27, 29, 34, 34, 38, 22, 22, 26, 27, 29, 34, 37, 40,
        22, 26, 27, 29, 32, 35, 40, 48, 26, 27, 29, 32, 35, 40, 48, 58, 26, 27, 29, 34, 38, 46, 56, 69, 27, 29, 35, 38, 46, 56, 69, 83,
    ];

    const int PictureI = 1, PictureP = 2, PictureB = 3;
    const int MbQuant = 1, MbForward = 2, MbBackward = 4, MbPattern = 8, MbIntra = 16;
    const int MbaStuffing = -1, MbaEscape = -2, DctEnd = -1, DctEscape = -2;

    readonly Bits _bits;
    readonly byte[] _intraMatrix = (byte[])DefaultIntra.Clone();
    readonly byte[] _nonIntraMatrix = Enumerable.Repeat((byte)16, 64).ToArray();
    readonly int[] _block = new int[64];
    readonly double[] _tmp = new double[64];

    int _mbWidth, _mbHeight, _mbCount;
    Mpeg1Frame? _forward, _backward, _current, _bFrame, _spare;
    bool _haveHeld; // há uma referência (I/P) decodificada esperando a vez de sair
    int _outIndex;

    // estado da imagem e da fatia
    int _pictureType, _quant, _mbAddress, _mbType, _prevMbType;
    int _fwdRSize, _bwdRSize;
    bool _fwdFull, _bwdFull;
    int _mvFwdH, _mvFwdV, _mvBwdH, _mvBwdV; // preditores (na unidade do vetor: pixel inteiro quando full_pel)
    readonly int[] _dcPred = new int[3];

    public int Width { get; private set; }
    public int Height { get; private set; }
    public double FrameRate { get; private set; }
    public List<string> Problems { get; } = [];
    /// <summary>Imagens decodificadas (todas as que tinham cabeçalho de imagem).</summary>
    public int Pictures { get; private set; }

    public Mpeg1Video(byte[] elementaryStream)
    {
        _bits = new Bits(elementaryStream);
        // Lê o primeiro cabeçalho de sequência para saber o tamanho antes do primeiro quadro.
        while (_bits.NextStartCode() is int code and >= 0)
            if (code == 0xB3) { ReadSequenceHeader(); break; }
        if (_mbCount == 0) Problems.Add("sem cabeçalho de sequência (00 00 01 B3)");
    }

    /// <summary>Próximo quadro na ordem de exibição, ou null no fim. O quadro vale até a chamada seguinte.</summary>
    public Mpeg1Frame? Next()
    {
        if (_mbCount == 0) return null;
        while (true)
        {
            int code = _bits.NextStartCode();
            if (code < 0 || code == 0xB7)
            {
                if (code == 0xB7 && _bits.HasMore) continue; // fim de sequência seguido de outra sequência
                if (!_haveHeld) return null;
                _haveHeld = false;
                return Emit(_backward!);
            }
            if (code == 0xB3) { ReadSequenceHeader(); continue; }
            if (code != 0x00) continue; // GOP, dados de usuário, extensões
            if (!DecodePicture(out var frame)) continue;
            if (frame != null) return frame;
        }
    }

    Mpeg1Frame Emit(Mpeg1Frame f)
    {
        f.Index = _outIndex++;
        return f;
    }

    void ReadSequenceHeader()
    {
        int w = _bits.Read(12), h = _bits.Read(12);
        _bits.Read(4); // aspecto
        int rate = _bits.Read(4);
        _bits.Read(18); _bits.Read(1); _bits.Read(10); _bits.Read(1); // taxa, marcador, VBV, restrito
        if (_bits.Read(1) == 1) for (int i = 0; i < 64; i++) _intraMatrix[ZigZag[i]] = (byte)_bits.Read(8);
        else DefaultIntra.CopyTo(_intraMatrix, 0);
        if (_bits.Read(1) == 1) for (int i = 0; i < 64; i++) _nonIntraMatrix[ZigZag[i]] = (byte)_bits.Read(8);
        else Array.Fill(_nonIntraMatrix, (byte)16);
        if (w == Width && h == Height && _mbCount > 0) return;
        if (w <= 0 || h <= 0) { Problems.Add($"tamanho inválido {w} × {h}"); return; }
        Width = w;
        Height = h;
        FrameRate = rate < FrameRates.Length ? FrameRates[rate] : 0;
        _mbWidth = (w + 15) >> 4;
        _mbHeight = (h + 15) >> 4;
        _mbCount = _mbWidth * _mbHeight;
        _forward = New(); _backward = New(); _spare = New(); _bFrame = New();
        _haveHeld = false;
    }

    Mpeg1Frame New() => new(Width, Height, _mbWidth, _mbHeight);

    // Decodifica uma imagem; devolve em frame o quadro que sai agora (a B na hora, a referência anterior quando chega uma I/P).
    bool DecodePicture(out Mpeg1Frame? frame)
    {
        frame = null;
        _bits.Read(10); // referência temporal
        _pictureType = _bits.Read(3);
        _bits.Read(16); // VBV delay
        if (_pictureType is < PictureI or > PictureB) return false; // D ou inválida
        if (_pictureType >= PictureP)
        {
            _fwdFull = _bits.Read(1) == 1;
            int f = _bits.Read(3);
            if (f == 0) { Problems.Add("forward_f_code 0"); return false; }
            _fwdRSize = f - 1;
        }
        if (_pictureType == PictureB)
        {
            _bwdFull = _bits.Read(1) == 1;
            int f = _bits.Read(3);
            if (f == 0) { Problems.Add("backward_f_code 0"); return false; }
            _bwdRSize = f - 1;
        }
        while (_bits.Read(1) == 1) _bits.Read(8); // informação extra

        Mpeg1Frame? held = null;
        if (_pictureType == PictureB) _current = _bFrame;
        else
        {
            // Nova referência: a de trás vira a da frente; a antiga da frente é reaproveitada para esta imagem.
            if (_haveHeld) held = _backward;
            (_forward, _backward, _spare) = (_backward, _spare, _forward);
            _current = _backward;
        }
        _current!.Type = " IPB"[_pictureType];
        Pictures++;

        // Fatias até o próximo código que não seja de fatia (dados de usuário e extensões entre o cabeçalho e as fatias são pulados).
        while (true)
        {
            int code = _bits.PeekStartCode();
            if (code is 0xB2 or 0xB5) { _bits.NextStartCode(); continue; }
            if (code is < 0x01 or > 0xAF) break;
            _bits.NextStartCode();
            DecodeSlice(code);
        }

        if (_pictureType == PictureB) frame = Emit(_current);
        else
        {
            if (held != null) frame = Emit(held);
            _haveHeld = true;
        }
        return true;
    }

    void DecodeSlice(int verticalPosition)
    {
        _quant = _bits.Read(5);
        while (_bits.Read(1) == 1) _bits.Read(8);
        _mbAddress = (verticalPosition - 1) * _mbWidth - 1;
        ResetDc();
        _mvFwdH = _mvFwdV = _mvBwdH = _mvBwdV = 0;
        _prevMbType = 0;
        bool first = true;
        try
        {
            do
            {
                if (!DecodeMacroblock(first)) return;
                first = false;
            } while (_mbAddress < _mbCount - 1 && !_bits.StartCodeAhead());
        }
        catch (InvalidDataException ex)
        {
            if (Problems.Count < 50) Problems.Add($"imagem {Pictures - 1}, fatia {verticalPosition}: {ex.Message}");
        }
    }

    void ResetDc() => _dcPred[0] = _dcPred[1] = _dcPred[2] = 128;

    bool DecodeMacroblock(bool firstInSlice)
    {
        int increment = 0;
        while (true)
        {
            int v = Vlc.Mba.Decode(_bits);
            if (v == MbaStuffing) continue;
            if (v == MbaEscape) { increment += 33; continue; }
            increment += v;
            break;
        }
        if (_mbAddress + increment >= _mbCount) throw new InvalidDataException($"macrobloco {_mbAddress + increment} fora da imagem");
        if (!firstInSlice && increment > 1)
        {
            ResetDc();
            if (_pictureType == PictureP) _mvFwdH = _mvFwdV = 0;
            for (int a = _mbAddress + 1; a < _mbAddress + increment; a++) PredictSkipped(a);
        }
        _mbAddress += increment;

        _mbType = _pictureType switch { PictureI => Vlc.MbTypeI.Decode(_bits), PictureP => Vlc.MbTypeP.Decode(_bits), _ => Vlc.MbTypeB.Decode(_bits) };
        if ((_mbType & MbQuant) != 0) _quant = _bits.Read(5);

        bool intra = (_mbType & MbIntra) != 0;
        if (intra) _mvFwdH = _mvFwdV = _mvBwdH = _mvBwdV = 0;
        else
        {
            ResetDc();
            if ((_mbType & MbForward) != 0)
            {
                _mvFwdH = MotionVector(_fwdRSize, _mvFwdH);
                _mvFwdV = MotionVector(_fwdRSize, _mvFwdV);
            }
            else if (_pictureType == PictureP) _mvFwdH = _mvFwdV = 0; // P sem vetor: vetor zero (e o preditor zera)
            if ((_mbType & MbBackward) != 0)
            {
                _mvBwdH = MotionVector(_bwdRSize, _mvBwdH);
                _mvBwdV = MotionVector(_bwdRSize, _mvBwdV);
            }
            Predict(_mbAddress, _pictureType == PictureP ? _mbType | MbForward : _mbType);
        }

        int cbp = (_mbType & MbPattern) != 0 ? Vlc.Cbp.Decode(_bits) : intra ? 63 : 0;
        int mx = _mbAddress % _mbWidth, my = _mbAddress / _mbWidth;
        for (int b = 0; b < 6; b++)
        {
            if ((cbp & (32 >> b)) == 0) continue;
            DecodeBlock(b, intra);
            var cur = _current!;
            if (b < 4) Put(cur.Y, cur.Stride, mx * 16 + (b & 1) * 8, my * 16 + (b >> 1) * 8, intra);
            else Put(b == 4 ? cur.Cb : cur.Cr, cur.Stride / 2, mx * 8, my * 8, intra);
        }
        _prevMbType = _mbType;
        return true;
    }

    // Macrobloco pulado: P copia da referência no mesmo lugar; B repete os vetores e as direções do anterior.
    void PredictSkipped(int address)
    {
        if (_pictureType == PictureP) Predict(address, MbForward, zero: true);
        else if (_pictureType == PictureB) Predict(address, _prevMbType & (MbForward | MbBackward));
    }

    int MotionVector(int rSize, int previous)
    {
        int f = 1 << rSize;
        int code = Vlc.Motion.Decode(_bits);
        int delta = code;
        if (f != 1 && code != 0)
        {
            int r = _bits.Read(rSize);
            delta = ((Math.Abs(code) - 1) << rSize) + r + 1;
            if (code < 0) delta = -delta;
        }
        int v = previous + delta;
        if (v > (f << 4) - 1) v -= f << 5;
        else if (v < -(f << 4)) v += f << 5;
        return v;
    }

    void Predict(int address, int directions, bool zero = false)
    {
        int mx = (address % _mbWidth) * 16, my = (address / _mbWidth) * 16;
        var dst = _current!;
        bool both = (directions & MbForward) != 0 && (directions & MbBackward) != 0;
        if ((directions & MbForward) != 0)
        {
            int h = zero ? 0 : _mvFwdH, v = zero ? 0 : _mvFwdV;
            if (_fwdFull) { h <<= 1; v <<= 1; }
            Compensate(_forward!, dst, mx, my, h, v, average: false);
        }
        if ((directions & MbBackward) != 0)
        {
            int h = _mvBwdH, v = _mvBwdV;
            if (_bwdFull) { h <<= 1; v <<= 1; }
            Compensate(_backward!, dst, mx, my, h, v, average: both);
        }
    }

    // Vetores em meio pixel da luma; o croma usa a metade (divisão que trunca para zero, como na norma).
    static void Compensate(Mpeg1Frame src, Mpeg1Frame dst, int x, int y, int mvH, int mvV, bool average)
    {
        int ls = src.Stride, lh = src.Y.Length / ls;
        Block(src.Y, dst.Y, ls, lh, x, y, 16, mvH, mvV, average);
        Block(src.Cb, dst.Cb, ls / 2, lh / 2, x / 2, y / 2, 8, mvH / 2, mvV / 2, average);
        Block(src.Cr, dst.Cr, ls / 2, lh / 2, x / 2, y / 2, 8, mvH / 2, mvV / 2, average);
    }

    static void Block(byte[] src, byte[] dst, int stride, int height, int x, int y, int size, int mvH, int mvV, bool average)
    {
        int sx = x + (mvH >> 1), sy = y + (mvV >> 1);
        int hx = mvH & 1, hy = mvV & 1;
        bool inside = sx >= 0 && sy >= 0 && sx + size + hx <= stride && sy + size + hy <= height;
        for (int j = 0; j < size; j++)
        {
            int d = (y + j) * stride + x;
            for (int i = 0; i < size; i++)
            {
                int p;
                int px = sx + i, py = sy + j;
                if (inside)
                {
                    int s = py * stride + px;
                    p = (hx, hy) switch
                    {
                        (0, 0) => src[s],
                        (1, 0) => (src[s] + src[s + 1] + 1) >> 1,
                        (0, 1) => (src[s] + src[s + stride] + 1) >> 1,
                        _ => (src[s] + src[s + 1] + src[s + stride] + src[s + stride + 1] + 2) >> 2,
                    };
                }
                else
                {
                    // Vetor que sai da imagem (fluxo inválido): repete a borda em vez de ler fora.
                    int At(int ax, int ay) => src[Math.Clamp(ay, 0, height - 1) * stride + Math.Clamp(ax, 0, stride - 1)];
                    p = (hx, hy) switch
                    {
                        (0, 0) => At(px, py),
                        (1, 0) => (At(px, py) + At(px + 1, py) + 1) >> 1,
                        (0, 1) => (At(px, py) + At(px, py + 1) + 1) >> 1,
                        _ => (At(px, py) + At(px + 1, py) + At(px, py + 1) + At(px + 1, py + 1) + 2) >> 2,
                    };
                }
                dst[d + i] = average ? (byte)((dst[d + i] + p + 1) >> 1) : (byte)p;
            }
        }
    }

    void DecodeBlock(int block, bool intra)
    {
        Array.Clear(_block);
        int n;
        if (intra)
        {
            int c = block < 4 ? 0 : block - 3;
            int size = (c == 0 ? Vlc.DcLuma : Vlc.DcChroma).Decode(_bits);
            int diff = 0;
            if (size > 0)
            {
                diff = _bits.Read(size);
                if ((diff & (1 << (size - 1))) == 0) diff -= (1 << size) - 1;
            }
            _dcPred[c] += diff;
            _block[0] = _dcPred[c] * 8;
            n = 1;
        }
        else n = 0;

        var matrix = intra ? _intraMatrix : _nonIntraMatrix;
        bool firstCoefficient = !intra;
        while (true)
        {
            int run, level;
            int v = (firstCoefficient ? Vlc.DctFirst : Vlc.DctNext).Decode(_bits);
            firstCoefficient = false;
            if (v == DctEnd) break;
            if (v == DctEscape)
            {
                run = _bits.Read(6);
                level = _bits.Read(8);
                if (level == 0) level = _bits.Read(8);
                else if (level == 128) level = _bits.Read(8) - 256;
                else if (level > 128) level -= 256;
            }
            else
            {
                run = v >> 8;
                level = v & 0xFF;
                if (_bits.Read(1) == 1) level = -level;
            }
            n += run;
            if (n > 63) throw new InvalidDataException("coeficiente além do 64º");
            int k = ZigZag[n++];
            // Reconstrução da norma (divisão que trunca para zero) e controle de descasamento (valor par vai para o ímpar).
            int recon = intra
                ? level * _quant * matrix[k] / 8
                : (2 * level + Math.Sign(level)) * _quant * matrix[k] / 16;
            if ((recon & 1) == 0) recon -= Math.Sign(recon);
            _block[k] = Math.Clamp(recon, -2048, 2047);
        }
        Idct(_block, _tmp);
    }

    // Soma (ou grava, no intra) o bloco 8 × 8 já transformado.
    void Put(byte[] plane, int stride, int x, int y, bool intra)
    {
        for (int j = 0; j < 8; j++)
        {
            int d = (y + j) * stride + x;
            for (int i = 0; i < 8; i++)
            {
                int v = _block[j * 8 + i] + (intra ? 0 : plane[d + i]);
                plane[d + i] = (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
            }
        }
    }

    static readonly double[] Cos = BuildCos();

    static double[] BuildCos()
    {
        var c = new double[64];
        for (int u = 0; u < 8; u++)
            for (int x = 0; x < 8; x++)
                c[u * 8 + x] = (u == 0 ? Math.Sqrt(0.5) : 1) / 2 * Math.Cos((2 * x + 1) * u * Math.PI / 16);
        return c;
    }

    /// <summary>IDCT 8 × 8 separável (linhas e colunas em double, arredondada); o bloco só com DC vira o valor constante.</summary>
    public static void Idct(int[] block, double[] tmp)
    {
        bool dcOnly = true;
        for (int i = 1; i < 64 && dcOnly; i++) dcOnly = block[i] == 0;
        if (dcOnly)
        {
            int dc = (int)Math.Round(block[0] / 8.0, MidpointRounding.AwayFromZero);
            Array.Fill(block, dc);
            return;
        }
        for (int v = 0; v < 8; v++) // linhas: tmp[v][x] = Σu c[u][x] F[v][u]
            for (int x = 0; x < 8; x++)
            {
                double s = 0;
                for (int u = 0; u < 8; u++) s += Cos[u * 8 + x] * block[v * 8 + u];
                tmp[v * 8 + x] = s;
            }
        for (int x = 0; x < 8; x++) // colunas
            for (int y = 0; y < 8; y++)
            {
                double s = 0;
                for (int v = 0; v < 8; v++) s += Cos[v * 8 + y] * tmp[v * 8 + x];
                block[y * 8 + x] = (int)Math.Round(s, MidpointRounding.AwayFromZero);
            }
    }

    /// <summary>Confere as tabelas de VLC: cada uma sem prefixo repetido e com a soma de Kraft ≤ 1. Vazio = tudo certo.</summary>
    public static List<string> CheckTables() => Vlc.Check();

    sealed class Bits(byte[] data)
    {
        readonly byte[] _d = data;
        long _pos;

        public bool HasMore => (_pos >> 3) < _d.Length;

        int B(long i) => i < _d.Length ? _d[i] : 0;

        public int Peek(int n)
        {
            long i = _pos >> 3;
            int sh = (int)(_pos & 7);
            uint w = (uint)(B(i) << 24 | B(i + 1) << 16 | B(i + 2) << 8 | B(i + 3));
            if (sh + n > 32) w = (w << sh) | (uint)(B(i + 4) >> (8 - sh)); else w <<= sh;
            return (int)(w >> (32 - n));
        }

        public int Read(int n)
        {
            if (n == 0) return 0;
            int v = Peek(n);
            _pos += n;
            return v;
        }

        public bool StartCodeAhead() => (_pos >> 3) + 3 >= _d.Length || Peek(23) == 0;

        /// <summary>Alinha ao byte e avança até depois do próximo código de início; −1 no fim.</summary>
        public int NextStartCode()
        {
            long i = (_pos + 7) >> 3;
            for (; i + 3 < _d.Length; i++)
                if (_d[i] == 0 && _d[i + 1] == 0 && _d[i + 2] == 1)
                {
                    _pos = (i + 4) * 8;
                    return _d[i + 3];
                }
            _pos = (long)_d.Length * 8;
            return -1;
        }

        public int PeekStartCode()
        {
            long save = _pos;
            int code = NextStartCode();
            _pos = save;
            return code;
        }
    }

    /// <summary>Árvore binária de um código de comprimento variável (anexo B da norma).</summary>
    sealed class Vlc
    {
        readonly List<int> _child = [0, 0]; // pares (bit 0, bit 1); 0 = sem filho
        readonly List<int> _value = [0];
        readonly List<bool> _leaf = [false];
        readonly List<string> _codes = [];
        public string Name { get; }

        Vlc(string name, IEnumerable<(string Code, int Value)> codes)
        {
            Name = name;
            foreach (var (code, value) in codes) Add(code.Replace(" ", ""), value);
        }

        void Add(string code, int value)
        {
            _codes.Add(code);
            int node = 0;
            foreach (char c in code)
            {
                int slot = node * 2 + (c - '0');
                if (_child[slot] == 0)
                {
                    _child[slot] = _leaf.Count;
                    _child.Add(0); _child.Add(0); _value.Add(0); _leaf.Add(false);
                }
                node = _child[slot];
            }
            _leaf[node] = true;
            _value[node] = value;
        }

        public int Decode(Bits bits)
        {
            int node = 0;
            while (!_leaf[node])
            {
                node = _child[node * 2 + bits.Read(1)];
                if (node == 0) throw new InvalidDataException($"código inválido na tabela {Name}");
            }
            return _value[node];
        }

        List<string> Problems()
        {
            var p = new List<string>();
            double kraft = 0;
            foreach (var c in _codes)
            {
                kraft += Math.Pow(2, -c.Length);
                foreach (var d in _codes)
                    if (!ReferenceEquals(c, d) && d.StartsWith(c, StringComparison.Ordinal)) p.Add($"{Name}: {c} é prefixo de {d}");
            }
            if (kraft > 1 + 1e-12) p.Add($"{Name}: soma de Kraft {kraft}");
            return p;
        }

        public static List<string> Check() =>
            new[] { Mba, MbTypeI, MbTypeP, MbTypeB, Cbp, Motion, DcLuma, DcChroma, DctFirst, DctNext }.SelectMany(v => v.Problems()).ToList();

        // B.1: incremento do endereço do macrobloco.
        public static readonly Vlc Mba = new("macroblock_address_increment",
        [
            ("1", 1), ("011", 2), ("010", 3), ("0011", 4), ("0010", 5), ("0001 1", 6), ("0001 0", 7), ("0000 111", 8), ("0000 110", 9),
            ("0000 1011", 10), ("0000 1010", 11), ("0000 1001", 12), ("0000 1000", 13), ("0000 0111", 14), ("0000 0110", 15),
            ("0000 0101 11", 16), ("0000 0101 10", 17), ("0000 0101 01", 18), ("0000 0101 00", 19), ("0000 0100 11", 20),
            ("0000 0100 10", 21), ("0000 0100 011", 22), ("0000 0100 010", 23), ("0000 0100 001", 24), ("0000 0100 000", 25),
            ("0000 0011 111", 26), ("0000 0011 110", 27), ("0000 0011 101", 28), ("0000 0011 100", 29), ("0000 0011 011", 30),
            ("0000 0011 010", 31), ("0000 0011 001", 32), ("0000 0011 000", 33), ("0000 0001 111", MbaStuffing), ("0000 0001 000", MbaEscape),
        ]);

        // B.2: tipo do macrobloco por tipo de imagem.
        public static readonly Vlc MbTypeI = new("macroblock_type I", [("1", MbIntra), ("01", MbIntra | MbQuant)]);

        public static readonly Vlc MbTypeP = new("macroblock_type P",
        [
            ("1", MbForward | MbPattern), ("01", MbPattern), ("001", MbForward), ("0001 1", MbIntra),
            ("0001 0", MbQuant | MbForward | MbPattern), ("0000 1", MbQuant | MbPattern), ("0000 01", MbQuant | MbIntra),
        ]);

        public static readonly Vlc MbTypeB = new("macroblock_type B",
        [
            ("10", MbForward | MbBackward), ("11", MbForward | MbBackward | MbPattern), ("010", MbBackward), ("011", MbBackward | MbPattern),
            ("0010", MbForward), ("0011", MbForward | MbPattern), ("0001 1", MbIntra), ("0001 0", MbQuant | MbForward | MbBackward | MbPattern),
            ("0000 11", MbQuant | MbForward | MbPattern), ("0000 10", MbQuant | MbBackward | MbPattern), ("0000 01", MbQuant | MbIntra),
        ]);

        // B.3: padrão de blocos codificados.
        public static readonly Vlc Cbp = new("coded_block_pattern",
        [
            ("111", 60), ("1101", 4), ("1100", 8), ("1011", 16), ("1010", 32), ("1001 1", 12), ("1001 0", 48), ("1000 1", 20), ("1000 0", 40),
            ("0111 1", 28), ("0111 0", 44), ("0110 1", 52), ("0110 0", 56), ("0101 1", 1), ("0101 0", 61), ("0100 1", 2), ("0100 0", 62),
            ("0011 11", 24), ("0011 10", 36), ("0011 01", 3), ("0011 00", 63), ("0010 111", 5), ("0010 110", 9), ("0010 101", 17),
            ("0010 100", 33), ("0010 011", 6), ("0010 010", 10), ("0010 001", 18), ("0010 000", 34), ("0001 1111", 7), ("0001 1110", 11),
            ("0001 1101", 19), ("0001 1100", 35), ("0001 1011", 13), ("0001 1010", 49), ("0001 1001", 21), ("0001 1000", 41),
            ("0001 0111", 14), ("0001 0110", 50), ("0001 0101", 22), ("0001 0100", 42), ("0001 0011", 15), ("0001 0010", 51),
            ("0001 0001", 23), ("0001 0000", 43), ("0000 1111", 25), ("0000 1110", 37), ("0000 1101", 26), ("0000 1100", 38),
            ("0000 1011", 29), ("0000 1010", 45), ("0000 1001", 53), ("0000 1000", 57), ("0000 0111", 30), ("0000 0110", 46),
            ("0000 0101", 54), ("0000 0100", 58), ("0000 0011 1", 31), ("0000 0011 0", 47), ("0000 0010 1", 55), ("0000 0010 0", 59),
            ("0000 0001 1", 27), ("0000 0001 0", 39),
        ]);

        // B.4: código do vetor de movimento; o último bit é o sinal (1 = negativo).
        public static readonly Vlc Motion = new("motion_code", MotionCodes());

        static IEnumerable<(string, int)> MotionCodes()
        {
            yield return ("1", 0);
            string[] bases = ["01", "001", "0001", "0000 11", "0000 101", "0000 100", "0000 011", "0000 0101 1", "0000 0101 0",
                "0000 0100 1", "0000 0100 01", "0000 0100 00", "0000 0011 11", "0000 0011 10", "0000 0011 01", "0000 0011 00"];
            for (int m = 1; m <= 16; m++)
            {
                yield return (bases[m - 1] + "0", m);
                yield return (bases[m - 1] + "1", -m);
            }
        }

        // B.5a/b: tamanho do DC.
        public static readonly Vlc DcLuma = new("dct_dc_size_luminance",
            [("100", 0), ("00", 1), ("01", 2), ("101", 3), ("110", 4), ("1110", 5), ("1111 0", 6), ("1111 10", 7), ("1111 110", 8)]);

        public static readonly Vlc DcChroma = new("dct_dc_size_chrominance",
            [("00", 0), ("01", 1), ("10", 2), ("110", 3), ("1110", 4), ("1111 0", 5), ("1111 10", 6), ("1111 110", 7), ("1111 1110", 8)]);

        // B.5c-f: coeficientes (corrida, nível), sem o bit de sinal. O primeiro coeficiente de um bloco não intra usa "1" para
        // (0, 1); os seguintes usam "10" para o fim do bloco e "11" para (0, 1).
        static IEnumerable<(string, int)> Coefficients(bool first)
        {
            static (string, int) C(string code, int run, int level) => (code, run << 8 | level);
            if (first) yield return C("1", 0, 1);
            else { yield return ("10", DctEnd); yield return C("11", 0, 1); }
            yield return ("0000 01", DctEscape);
            (string, int, int)[] table =
            [
                ("011", 1, 1), ("0100", 0, 2), ("0101", 2, 1), ("0010 1", 0, 3), ("0011 1", 3, 1), ("0011 0", 4, 1), ("0001 10", 1, 2),
                ("0001 11", 5, 1), ("0001 01", 6, 1), ("0001 00", 7, 1), ("0000 110", 0, 4), ("0000 100", 2, 2), ("0000 111", 8, 1),
                ("0000 101", 9, 1), ("0010 0110", 0, 5), ("0010 0001", 0, 6), ("0010 0101", 1, 3), ("0010 0100", 3, 2),
                ("0010 0111", 10, 1), ("0010 0011", 11, 1), ("0010 0010", 12, 1), ("0010 0000", 13, 1), ("0000 0010 10", 0, 7),
                ("0000 0011 00", 1, 4), ("0000 0010 11", 2, 3), ("0000 0011 11", 4, 2), ("0000 0010 01", 5, 2), ("0000 0011 10", 14, 1),
                ("0000 0011 01", 15, 1), ("0000 0010 00", 16, 1), ("0000 0001 1101", 0, 8), ("0000 0001 1000", 0, 9),
                ("0000 0001 0011", 0, 10), ("0000 0001 0000", 0, 11), ("0000 0001 1011", 1, 5), ("0000 0001 0100", 2, 4),
                ("0000 0001 1100", 3, 3), ("0000 0001 0010", 4, 3), ("0000 0001 1110", 6, 2), ("0000 0001 0101", 7, 2),
                ("0000 0001 0001", 8, 2), ("0000 0001 1111", 17, 1), ("0000 0001 1010", 18, 1), ("0000 0001 1001", 19, 1),
                ("0000 0001 0111", 20, 1), ("0000 0001 0110", 21, 1), ("0000 0000 1101 0", 0, 12), ("0000 0000 1100 1", 0, 13),
                ("0000 0000 1100 0", 0, 14), ("0000 0000 1011 1", 0, 15), ("0000 0000 1011 0", 1, 6), ("0000 0000 1010 1", 1, 7),
                ("0000 0000 1010 0", 2, 5), ("0000 0000 1001 1", 3, 4), ("0000 0000 1001 0", 5, 3), ("0000 0000 1000 1", 9, 2),
                ("0000 0000 1000 0", 10, 2), ("0000 0000 1111 1", 22, 1), ("0000 0000 1111 0", 23, 1), ("0000 0000 1110 1", 24, 1),
                ("0000 0000 1110 0", 25, 1), ("0000 0000 1101 1", 26, 1), ("0000 0000 0111 11", 0, 16), ("0000 0000 0111 10", 0, 17),
                ("0000 0000 0111 01", 0, 18), ("0000 0000 0111 00", 0, 19), ("0000 0000 0110 11", 0, 20), ("0000 0000 0110 10", 0, 21),
                ("0000 0000 0110 01", 0, 22), ("0000 0000 0110 00", 0, 23), ("0000 0000 0101 11", 0, 24), ("0000 0000 0101 10", 0, 25),
                ("0000 0000 0101 01", 0, 26), ("0000 0000 0101 00", 0, 27), ("0000 0000 0100 11", 0, 28), ("0000 0000 0100 10", 0, 29),
                ("0000 0000 0100 01", 0, 30), ("0000 0000 0100 00", 0, 31), ("0000 0000 0011 000", 0, 32), ("0000 0000 0010 111", 0, 33),
                ("0000 0000 0010 110", 0, 34), ("0000 0000 0010 101", 0, 35), ("0000 0000 0010 100", 0, 36), ("0000 0000 0010 011", 0, 37),
                ("0000 0000 0010 010", 0, 38), ("0000 0000 0010 001", 0, 39), ("0000 0000 0010 000", 0, 40), ("0000 0000 0011 111", 1, 8),
                ("0000 0000 0011 110", 1, 9), ("0000 0000 0011 101", 1, 10), ("0000 0000 0011 100", 1, 11), ("0000 0000 0011 011", 1, 12),
                ("0000 0000 0011 010", 1, 13), ("0000 0000 0011 001", 1, 14), ("0000 0000 0001 0011", 1, 15),
                ("0000 0000 0001 0010", 1, 16), ("0000 0000 0001 0001", 1, 17), ("0000 0000 0001 0000", 1, 18),
                ("0000 0000 0001 0100", 6, 3), ("0000 0000 0001 1010", 11, 2), ("0000 0000 0001 1001", 12, 2),
                ("0000 0000 0001 1000", 13, 2), ("0000 0000 0001 0111", 14, 2), ("0000 0000 0001 0110", 15, 2),
                ("0000 0000 0001 0101", 16, 2), ("0000 0000 0001 1111", 27, 1), ("0000 0000 0001 1110", 28, 1),
                ("0000 0000 0001 1101", 29, 1), ("0000 0000 0001 1100", 30, 1), ("0000 0000 0001 1011", 31, 1),
            ];
            foreach (var (code, run, level) in table) yield return C(code, run, level);
        }

        public static readonly Vlc DctFirst = new("dct_coeff_first", Coefficients(first: true));
        public static readonly Vlc DctNext = new("dct_coeff_next", Coefficients(first: false));
    }
}
