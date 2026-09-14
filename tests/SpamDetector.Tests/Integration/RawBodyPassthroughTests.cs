using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SpamDetector.Functions;
using SpamDetector.Models;
using Xunit;

namespace SpamDetector.Tests.Integration;

public sealed class RawBodyPassthroughTests
{
    [Fact]
    public async Task Raw_body_with_html_mime_and_headers_is_passed_through_unchanged()
    {
        const string rawEmail =
            "From: attacker@example.tld\r\n" +
            "Subject: Win!!!\r\n" +
            "Content-Type: multipart/mixed; boundary=abc\r\n\r\n" +
            "--abc\r\nContent-Type: text/html\r\n\r\n" +
            "<html><body><h1>Click <a href=\"http://x.tld\">here</a></h1></body></html>\r\n--abc--";

        var classifier = new FakeSpamClassifier(TestFactory.Spam());
        SpamCheckFunction function = TestFactory.CreateFunction(classifier);
        HttpRequest request = TestFactory.CreateHttpRequest(rawEmail, apiKey: "sk-test-key");

        IActionResult actionResult = await function.Run(request, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(actionResult);
        var body = Assert.IsType<ApiResult<SpamResult>>(objectResult.Value);
        Assert.Equal(200, body.HttpCode);
        Assert.NotNull(body.Result);
        // Exact raw body forwarded to the classifier with no parsing/normalization.
        Assert.Equal(rawEmail, classifier.LastRequest!.EmailContent);
    }
}
