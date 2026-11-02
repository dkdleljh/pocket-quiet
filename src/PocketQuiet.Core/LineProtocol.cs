namespace PocketQuiet.Core;

public static class LineProtocol
{
    public static async Task<string> ReadAsync(TextReader reader, CancellationToken cancellationToken, int limit = Wire.MaxLineLength)
    {
        var buffer = new char[Math.Min(256, limit + 1)];
        var line = new System.Text.StringBuilder();
        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) throw new EndOfStreamException("완료되지 않은 요청입니다. 줄바꿈으로 끝내세요.");
            int newline = Array.IndexOf(buffer, '\n', 0, read);
            int count = newline >= 0 ? newline : read;
            if (line.Length + count > limit) throw new InvalidDataException("요청 크기 초과");
            line.Append(buffer, 0, count);
            if (newline >= 0) return line.ToString().TrimEnd('\r');
        }
    }
}
