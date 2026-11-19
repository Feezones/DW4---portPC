using DW4.Net;

namespace DW4.Server;

/// <summary>
/// Guilda no teste local (DW4_DEV=1), em memória enquanto o servidor roda: as mesmas regras do Dw4GuildService da DMO.API
/// (cargos 1 Mestre .. 4 Novato, nível pelo XP, vagas 20 + 5 por nível, bônus de 1% por nível acima do 1, baú de 60 tipos e 999
/// por pilha). Nunca ligar em produção.
/// </summary>
public sealed partial class DevApi
{
    sealed class DevGuild
    {
        public long Id;
        public string Name = "", Tag = "", Notice = "";
        public int Level = 1;
        public long Xp, BankBit;
        public readonly List<GuildMemberData> Members = [];
        public readonly Dictionary<(string, int), int> Bank = [];
        public readonly List<GuildLogData> Log = [];
    }

    readonly Dictionary<long, DevGuild> _guilds = [];
    readonly Dictionary<long, long> _guildOf = [];
    long _nextGuild;

    static long XpFor(int level) => 500L * (level - 1) * level;
    static int LevelFor(long xp) { int l = 1; while (l < 10 && xp >= XpFor(l + 1)) l++; return l; }
    static int MaxMembers(int level) => 20 + 5 * (level - 1);

    public Task<GuildReply> GuildAsync(GuildRequest q)
    {
        lock (_lock) return Task.FromResult(Guild(q));
    }

    GuildReply Guild(GuildRequest q)
    {
        DevGuild? g = _guildOf.TryGetValue(q.AccountId, out var gid) ? _guilds[gid] : null;
        var me = g?.Members.FirstOrDefault(m => m.AccountId == q.AccountId);
        GuildReply Ok(string msg = "") => new(true, msg, g == null ? null : Data(g));
        GuildReply Fail(string msg) => new(false, msg, g == null ? null : Data(g));
        void Log(string kind, string? table = null, int? row = null, int count = 0, long bit = 0, string? text = null) =>
            g!.Log.Insert(0, new GuildLogData(DateTime.UtcNow, q.Name, kind, table, row, count, bit, text));
        switch (q.Action)
        {
            case "get": return Ok();
            case "create":
            {
                if (g != null) return Fail("Você já está em uma guilda.");
                var name = q.GuildName.Trim(); var tag = q.Tag.Trim().ToUpperInvariant();
                if (name.Length is < 3 or > 16) return Fail("Nome da guilda: de 3 a 16 letras ou números.");
                if (tag.Length is < 2 or > 4 || !tag.All(char.IsAsciiLetterOrDigit)) return Fail("Tag da guilda: de 2 a 4 letras (sem acento) ou números.");
                if (_guilds.Values.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return Fail($"Já existe uma guilda chamada {name}.");
                if (_guilds.Values.Any(x => x.Tag == tag)) return Fail($"A tag [{tag}] já é de outra guilda.");
                g = new DevGuild { Id = ++_nextGuild, Name = name, Tag = tag };
                g.Members.Add(new GuildMemberData(q.AccountId, q.Name, 1, 0, DateTime.UtcNow));
                _guilds[g.Id] = g; _guildOf[q.AccountId] = g.Id;
                Log("create", text: name);
                return Ok($"Guilda {name} [{tag}] criada.");
            }
            case "can-invite":
                if (g == null || me == null) return Fail("Você não está em uma guilda.");
                if (me.Rank > 2) return Fail("Só o Mestre e os Sub-mestres convidam.");
                if (g.Members.Count >= MaxMembers(g.Level)) return Fail($"A guilda está cheia ({MaxMembers(g.Level)}).");
                if (_guildOf.TryGetValue(q.TargetAccountId, out var other)) return Fail(other == g.Id ? "Esse jogador já está na sua guilda." : "Esse jogador já está em outra guilda.");
                return Ok();
            case "join":
            {
                if (g != null) return Fail("Você já está em uma guilda.");
                if (!_guilds.TryGetValue(q.GuildId, out var target)) return Fail("Essa guilda não existe mais.");
                if (target.Members.FirstOrDefault(m => m.AccountId == q.InviterAccountId) is not { Rank: <= 2 }) return Fail("O convite não vale mais.");
                if (target.Members.Count >= MaxMembers(target.Level)) return Fail($"A guilda {target.Name} está cheia ({MaxMembers(target.Level)}).");
                g = target;
                g.Members.Add(new GuildMemberData(q.AccountId, q.Name, 4, 0, DateTime.UtcNow));
                _guildOf[q.AccountId] = g.Id;
                Log("join");
                return Ok($"Você entrou na guilda {g.Name}.");
            }
        }
        if (g == null || me == null) return new(false, "Você não está em uma guilda.", null);
        var tgt = g.Members.FirstOrDefault(m => m.AccountId == q.TargetAccountId);
        void Set(GuildMemberData m) { int i = g.Members.FindIndex(x => x.AccountId == m.AccountId); g.Members[i] = m; }
        switch (q.Action)
        {
            case "leave":
                if (me.Rank == 1 && g.Members.Count > 1) return Fail("Passe o cargo de Mestre para outro membro antes de sair.");
                g.Members.Remove(me); _guildOf.Remove(q.AccountId);
                if (g.Members.Count == 0) { _guilds.Remove(g.Id); return new(true, $"A guilda {g.Name} acabou.", null); }
                Log("leave");
                return new(true, $"Você saiu da guilda {g.Name}.", null);
            case "kick":
                if (tgt == null) return Fail("Esse jogador não está na sua guilda.");
                if (me.Rank > 2 || tgt.Rank <= me.Rank) return Fail("Você só pode tirar membros de cargo abaixo do seu.");
                g.Members.Remove(tgt); _guildOf.Remove(tgt.AccountId);
                Log("kick", text: tgt.Name);
                return Ok($"{tgt.Name} foi tirado da guilda.");
            case "rank":
                if (me.Rank > 2) return Fail("Só o Mestre e os Sub-mestres mudam cargos.");
                if (tgt == null) return Fail("Esse jogador não está na sua guilda.");
                if (q.Rank is < 2 or > 4) return Fail(q.Rank == 1 ? "Para fazer outro Mestre, use Passar o cargo de Mestre." : "Cargo inválido.");
                if (tgt.AccountId == me.AccountId || tgt.Rank <= me.Rank || q.Rank <= me.Rank)
                    return Fail(me.Rank == 2 && q.Rank == 2 ? "Só o Mestre nomeia Sub-mestres." : "Você só pode mudar o cargo de membros abaixo do seu, para um cargo abaixo do seu.");
                Set(tgt with { Rank = q.Rank });
                Log("rank", count: q.Rank, text: tgt.Name);
                return Ok($"{tgt.Name} agora é {RankName(q.Rank)}.");
            case "transfer":
                if (me.Rank != 1) return Fail("Só o Mestre passa o cargo de Mestre.");
                if (tgt == null || tgt.AccountId == me.AccountId) return Fail("Esse jogador não está na sua guilda.");
                Set(me with { Rank = 2 }); Set(tgt with { Rank = 1 });
                Log("master", text: tgt.Name);
                return Ok($"{tgt.Name} agora é o Mestre da guilda.");
            case "notice":
                if (me.Rank > 2) return Fail("Só o Mestre e os Sub-mestres mudam o aviso.");
                g.Notice = string.Join(' ', q.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                if (g.Notice.Length > 120) g.Notice = g.Notice[..120];
                Log("notice", text: g.Notice);
                return Ok(g.Notice.Length == 0 ? "Aviso apagado." : "Aviso da guilda atualizado.");
            case "xp":
            {
                if (q.Amount <= 0) return Ok();
                long add = Math.Min(q.Amount, 20_000);
                g.Xp += add;
                Set(me with { Contribution = me.Contribution + add });
                int level = LevelFor(g.Xp);
                if (level <= g.Level) return Ok();
                g.Level = level;
                Log("level", count: level);
                return Ok($"Nível {level}");
            }
            case "deposit":
            {
                var items = q.Items ?? [];
                if (q.Bit < 0 || (items.Count == 0 && q.Bit == 0)) return Fail("Pedido inválido.");
                var bank = new Dictionary<(string, int), int>(g.Bank);
                foreach (var i in items) bank[(i.Table, i.Row)] = bank.GetValueOrDefault((i.Table, i.Row)) + i.Count;
                if (bank.Count > 60 || bank.Values.Any(c => c > 999) || g.BankBit + q.Bit > 99_999_999) return Fail("O baú da guilda está cheio.");
                foreach (var (k, v) in bank) g.Bank[k] = v;
                g.BankBit += q.Bit;
                foreach (var i in items) Log("deposit", i.Table, i.Row, i.Count);
                if (q.Bit > 0) Log("deposit", bit: q.Bit);
                return Ok();
            }
            case "can-withdraw" or "withdraw":
            {
                var item = q.Items?.FirstOrDefault();
                if (item != null && me.Rank > 3) return Fail("Novatos ainda não tiram itens do baú.");
                if (q.Bit > 0 && me.Rank > 2) return Fail("Só o Mestre e os Sub-mestres tiram BIT do baú.");
                if (q.Action == "can-withdraw") return Ok();
                if (item != null && g.Bank.GetValueOrDefault((item.Table, item.Row)) < item.Count) return Fail("O baú não tem tanto assim.");
                if (q.Bit > g.BankBit) return Fail("O baú não tem tanto assim.");
                if (item != null)
                {
                    int left = g.Bank[(item.Table, item.Row)] - item.Count;
                    if (left == 0) g.Bank.Remove((item.Table, item.Row)); else g.Bank[(item.Table, item.Row)] = left;
                    Log("withdraw", item.Table, item.Row, item.Count);
                }
                g.BankBit -= q.Bit;
                if (q.Bit > 0) Log("withdraw", bit: q.Bit);
                return Ok();
            }
            case "refund":
                foreach (var i in q.Items ?? []) g.Bank[(i.Table, i.Row)] = g.Bank.GetValueOrDefault((i.Table, i.Row)) + i.Count;
                g.BankBit += q.Bit;
                return Ok();
            case "log":
                return new(true, "", Data(g), g.Log.Take(q.Take <= 0 ? 40 : q.Take).ToList());
            default:
                return Fail("Ação desconhecida.");
        }
    }

    static string RankName(int rank) => rank switch { 1 => "Mestre", 2 => "Sub-mestre", 3 => "Membro", _ => "Novato" };

    static GuildData Data(DevGuild g) => new(g.Id, g.Name, g.Tag, g.Notice, g.Level, g.Xp, XpFor(g.Level), g.Level >= 10 ? 0 : XpFor(g.Level + 1),
        MaxMembers(g.Level), g.Level - 1, g.BankBit, 0, g.Members.OrderBy(m => m.Rank).ToList(),
        g.Bank.OrderBy(kv => kv.Key.Item1).ThenBy(kv => kv.Key.Item2).Select(kv => new GuildBankEntry(kv.Key.Item1, kv.Key.Item2, kv.Value)).ToList());
}
