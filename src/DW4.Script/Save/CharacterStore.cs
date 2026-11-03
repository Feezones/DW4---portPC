namespace DW4.Script.Save;

/// <summary>
/// Personagens de uma conta: 4 vagas, cada uma vazia ("Criar novo Digimon") ou com o personagem criado ali. DECISÃO DO PORT
/// (pedido do responsável, docs/decisoes-port-pc.md): as vagas são da conta/jogador. Hoje só existe a implementação local
/// (<see cref="LocalCharacterStore"/>); na Fase 6 uma implementação no servidor troca esta sem mudar as telas.
/// </summary>
public interface ICharacterStore
{
    const int Slots = 4;

    /// <summary>As 4 vagas da conta (null = vazia).</summary>
    IReadOnlyList<CharacterSave?> List(string account);
    CharacterSave? Load(string account, int slot);
    void Save(string account, int slot, CharacterSave save);
    void Delete(string account, int slot);
}

/// <summary>Vagas em arquivos: &lt;raiz&gt;/accounts/&lt;conta&gt;/slotN.json (N = 1..4). O cliente usa user:// como raiz.</summary>
public sealed class LocalCharacterStore(string root) : ICharacterStore
{
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
}
