namespace DW4.Script.Save;

/// <summary>
/// Personagens de uma conta: 4 vagas, cada uma vazia ("Criar novo Digimon") ou com o personagem criado ali. DECISÃO DO PORT
/// (pedido do responsável, docs/decisoes-port-pc.md): as vagas são da conta/jogador. DECISÃO DO RESPONSÁVEL (2026-11-04: "saves no
/// banco ... tudo tem que ser online, nada local"): no jogo as vagas ficam no servidor (OnlineCharacterStore do cliente, pela
/// conexão do DW4 até a DMO.API); <see cref="LocalCharacterStore"/> sobra só para o editor sem servidor e para ler as vagas antigas
/// do aparelho na migração (<see cref="SlotMigration"/>).
/// As operações são assíncronas (vão e voltam do servidor).
/// </summary>
public interface ICharacterStore
{
    const int Slots = 4;

    /// <summary>As 4 vagas da conta (null = vazia).</summary>
    Task<IReadOnlyList<CharacterSave?>> ListAsync(string account);
    Task SaveAsync(string account, int slot, CharacterSave save);
    Task DeleteAsync(string account, int slot);
}

/// <summary>Vagas em arquivos: &lt;raiz&gt;/accounts/&lt;conta&gt;/slotN.json (N = 1..4). O cliente usa user:// como raiz.</summary>
public sealed class LocalCharacterStore(string root) : ICharacterStore
{
    /// <summary>Sufixo do arquivo de uma vaga que já subiu para a conta no servidor (fica como cópia; o jogo não lê mais).</summary>
    public const string MigratedSuffix = ".enviado";

    public string Root { get; } = root;

    public string PathOf(string account, int slot)
    {
        if (slot is < 0 or >= ICharacterStore.Slots) throw new ArgumentOutOfRangeException(nameof(slot));
        if (account.Length == 0 || account.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || account is "." or "..")
            throw new ArgumentException($"nome de conta inválido: \"{account}\"", nameof(account));
        return Path.Combine(Root, "accounts", account, $"slot{slot + 1}.json");
    }

    public IReadOnlyList<CharacterSave?> List(string account) =>
        Enumerable.Range(0, ICharacterStore.Slots).Select(i => Load(account, i)).ToList();

    public CharacterSave? Load(string account, int slot)
    {
        var path = PathOf(account, slot);
        return File.Exists(path) ? CharacterSave.FromJson(File.ReadAllText(path)) : null;
    }

    public void Save(string account, int slot, CharacterSave save)
    {
        var path = PathOf(account, slot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Grava ao lado e troca, para um save interrompido não estragar o anterior.
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, save.ToJson());
        File.Move(tmp, path, overwrite: true);
    }

    public void Delete(string account, int slot)
    {
        var path = PathOf(account, slot);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>
    /// Vaga local que já subiu para o servidor: o arquivo vira slotN.json.enviado (ou .enviado2, 3... se já existe), para o jogo
    /// não subir de novo e o jogador ainda ter a cópia.
    /// </summary>
    public void MarkMigrated(string account, int slot)
    {
        var path = PathOf(account, slot);
        if (!File.Exists(path)) return;
        var target = path + MigratedSuffix;
        for (int n = 2; File.Exists(target); n++) target = path + MigratedSuffix + n;
        File.Move(path, target);
    }

    public Task<IReadOnlyList<CharacterSave?>> ListAsync(string account) => Task.FromResult(List(account));
    public Task SaveAsync(string account, int slot, CharacterSave save) { Save(account, slot, save); return Task.CompletedTask; }
    public Task DeleteAsync(string account, int slot) { Delete(account, slot); return Task.CompletedTask; }
}

/// <summary>
/// Migração das vagas antigas do aparelho para a conta no servidor (2026-11-04). Cada personagem local vai para a mesma vaga se
/// ela está livre no servidor, senão para a primeira livre; sem vaga livre, fica no aparelho (e o menu avisa).
/// </summary>
public static class SlotMigration
{
    /// <summary>Destino de cada vaga local ocupada: (vaga local, vaga no servidor ou −1 = não cabe).</summary>
    public static List<(int Local, int Target)> Plan(IReadOnlyList<CharacterSave?> server, IReadOnlyList<CharacterSave?> local)
    {
        var taken = new bool[ICharacterStore.Slots];
        for (int i = 0; i < ICharacterStore.Slots; i++) taken[i] = i < server.Count && server[i] != null;
        var plan = new List<(int, int)>();
        // Primeiro quem cabe na mesma vaga (o jogador reconhece o lugar), depois os outros na primeira livre.
        var pending = new List<int>();
        for (int i = 0; i < Math.Min(local.Count, ICharacterStore.Slots); i++)
        {
            if (local[i] == null) continue;
            if (!taken[i]) { taken[i] = true; plan.Add((i, i)); }
            else pending.Add(i);
        }
        foreach (var i in pending)
        {
            int free = Array.IndexOf(taken, false);
            if (free >= 0) taken[free] = true;
            plan.Add((i, free));
        }
        return plan.OrderBy(p => p.Item1).ToList();
    }
}
