using System.Collections.Concurrent;
using System.Text.Json;

namespace DaouCalendarOverlay.Services;

/// <summary>
/// JSON 파일을 원자적으로 저장하는 공통 구현.
/// 같은 경로에 대한 저장은 프로세스 안에서 직렬화되고(경로별 세마포어),
/// 임시 파일은 대상과 같은 디렉터리에 GUID 이름으로 만든 뒤 <see cref="File.Move(string, string, bool)"/>로 교체한다.
/// 어떤 실패도 호출자에게 예외로 전달하지 않고 false 로 알린다.
/// </summary>
public static class AtomicJsonFileWriter
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 대상 파일과 같은 디렉터리에 만들 임시 파일 경로를 만든다.
    /// 다른 볼륨으로 나가면 File.Move 가 원자적이지 않으므로 %TEMP% 를 쓰지 않는다.
    /// </summary>
    public static string CreateTempPath(string finalPath) =>
        finalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";

    /// <summary>
    /// <paramref name="value"/>를 JSON 으로 직렬화해 <paramref name="finalPath"/>에 원자적으로 저장한다.
    /// 성공하면 true, 실패하면 로그를 남기고 false 를 돌려준다(예외를 던지지 않는다).
    /// </summary>
    public static async Task<bool> WriteAsync<T>(string finalPath, T value, JsonSerializerOptions options, string logCategory)
    {
        string fullPath;
        try
        {
            // 게이트 키를 만드는 단계에서도 예외가 밖으로 나가면 안 된다(잘못된 경로·너무 긴 경로).
            fullPath = Path.GetFullPath(finalPath);
        }
        catch (Exception ex)
        {
            LogService.Error(logCategory, "저장 실패: 사용할 수 없는 경로", ex);
            return false;
        }

        var gate = Gates.GetOrAdd(fullPath, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);

        string? temp = null;
        try
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            temp = CreateTempPath(finalPath);
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, value, options).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }

            File.Move(temp, finalPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            // 저장한 값(파일 내용)은 로그에 남기지 않는다. 경로와 예외만 남긴다.
            LogService.Error(logCategory, $"저장 실패: {finalPath}", ex);
            return false;
        }
        finally
        {
            try
            {
                if (temp is not null && File.Exists(temp))
                    File.Delete(temp);
            }
            catch (Exception ex)
            {
                LogService.Warn(logCategory, "임시 파일 정리 실패", ex);
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
