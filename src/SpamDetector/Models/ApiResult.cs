namespace SpamDetector.Models;

/// <summary>
/// Generic response envelope. The HTTP response status equals <see cref="HttpCode"/>.
/// </summary>
public class ApiResult<T>
{
    public T? Result { get; set; }

    public int HttpCode { get; set; } = 200;

    public bool IsError { get; set; } = false;

    public string? ErrorMessage { get; set; }
}
