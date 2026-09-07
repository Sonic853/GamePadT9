namespace GamePadT9;

internal sealed class SymbolMenu
{
    private static readonly Candidate[] Symbols =
    [
        new("，", "逗号"), new("。", "句号"), new("？", "问号"), new("！", "感叹号"), new("、", "顿号"), new("：", "冒号"), new("；", "分号"), new("（", "左括号"), new("）", "右括号"),
        new("“", "左引号"), new("”", "右引号"), new("《", "左书名号"), new("》", "右书名号"), new("…", "省略号"), new("—", "破折号"), new("·", "间隔号"), new("「", "左直角引号"), new("」", "右直角引号"),
        new(".", "点"), new(",", "英文逗号"), new("?", "英文问号"), new("!", "英文感叹号"), new("@", "艾特"), new("/", "斜杠"), new("-", "短横线"), new("_", "下划线"), new("#", "井号")
    ];
    public bool Visible { get; private set; }
    private int index;
    public EngineView View => new("常用符号", index / 9, index / 9 == 2, index % 9, Symbols.Skip(index / 9 * 9).Take(9).ToArray());
    public void Open() { index = 0; Visible = true; }
    public void Close() => Visible = false;
    public void Move(int offset) => index = Math.Clamp(index + offset, 0, Symbols.Length - 1);
    public void Page(int offset) => index = Math.Clamp(index / 9 + offset, 0, 2) * 9;
    public string Select(int? pageIndex) => pageIndex is int i && (uint)i < 9 ? Symbols[index / 9 * 9 + i].Text : Symbols[index].Text;
}
