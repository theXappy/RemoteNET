using System.Collections;
using System.Collections.Specialized;
using System.Net;
using ScubaDiver.API.Protocol.SimpleHttp;

namespace ScubaDiver.API.Tests
{
    [TestFixture]
    public class SimpleHttpEncoderTests
    {

        [Test]
        public void TryParseHttpRequest_ValidRequest_ReturnsTrueAndParsesCorrectly()
        {
            // Arrange
            string request = "GET /path/to/resource?param=value HTTP/1.1\r\n" +
                             "Host: example.com\r\n" +
                             "Content-Length: 10\r\n" +
                             "\r\n" +
                             "0123456789";
            byte[] requestData = System.Text.Encoding.UTF8.GetBytes(request);

            // Act
            int consumedBytes = SimpleHttpEncoder.TryParseHttpRequest(requestData, out HttpRequestSummary summary);

            // Assert
            Assert.That(consumedBytes, Is.GreaterThan(0));
            Assert.That(summary.Method, Is.EqualTo("GET"));
            Assert.That(summary.Url, Is.EqualTo("/path/to/resource"));
            Assert.That(summary.QueryString.Count, Is.EqualTo(1));
            Assert.That(summary.QueryString["param"], Is.EqualTo("value"));
            Assert.That(new byte[] { 48, 49, 50, 51, 52, 53, 54, 55, 56, 57 }.SequenceEqual(summary.Body));
        }
        [Test]
        public void TryParseHttpRequest_ValidRequestTwoQueryArgs_ReturnsTrueAndParsesCorrectly()
        {
            // Arrange
            string request = "GET /path/to/resource?param=value&param2=lol HTTP/1.1\r\n" +
                             "Host: example.com\r\n" +
                             "Content-Length: 10\r\n" +
                             "\r\n" +
                             "0123456789";
            byte[] requestData = System.Text.Encoding.UTF8.GetBytes(request);

            // Act
            int consumedBytes = SimpleHttpEncoder.TryParseHttpRequest(requestData, out HttpRequestSummary summary);

            // Assert
            Assert.That(consumedBytes > 0, Is.True);
            Assert.That(summary.Method, Is.EqualTo("GET"));
            Assert.That(summary.Url, Is.EqualTo("/path/to/resource"));
            Assert.That(summary.QueryString.Count, Is.EqualTo(2));
            Assert.That(summary.QueryString["param"], Is.EqualTo("value"));
            Assert.That(summary.QueryString["param2"], Is.EqualTo("lol"));
            Assert.That(new byte[] { 48, 49, 50, 51, 52, 53, 54, 55, 56, 57 }.SequenceEqual(summary.Body));
        }

        [Test]
        public void TryParseHttpRequest_ValidPostRequest_ReturnsTrueAndParsesCorrectly()
        {
            // Arrange
            string request = "POST /api/data HTTP/1.1\r\n" +
                             "Host: example.com\r\n" +
                             "Content-Length: 13\r\n" +
                             "\r\n" +
                             "Hello, world!";
            byte[] requestData = System.Text.Encoding.UTF8.GetBytes(request);

            // Act
            int consumedBytes = SimpleHttpEncoder.TryParseHttpRequest(requestData, out HttpRequestSummary summary);

            // Assert
            Assert.That(consumedBytes > 0, Is.True);
            Assert.That(summary.Method, Is.EqualTo("POST"));
            Assert.That(summary.Url, Is.EqualTo("/api/data"));
            Assert.That(summary.QueryString.Count, Is.EqualTo(0));
            Assert.That(System.Text.Encoding.UTF8.GetBytes("Hello, world!").SequenceEqual(summary.Body));
        }

        [Test]
        public void EncodeAndDecode_GetRequestSummary_SuccessfullyEncodesAndDecodes()
        {
            // Arrange
            HttpRequestSummary inputSummary = new HttpRequestSummary
            {
                Method = "GET",
                Url = "/path/to/resource",
                QueryString = new NameValueCollection()
                {
                    ["param"] = "value"
                },
                Body = Array.Empty<byte>()
            };

            // Act
            byte[] encodedBytes;
            Assert.That(SimpleHttpEncoder.TryEncodeHttpRequest(inputSummary, out encodedBytes));

            HttpRequestSummary decodedSummary;
            int consumedBytes = SimpleHttpEncoder.TryParseHttpRequest(encodedBytes, out decodedSummary);

            // Assert
            Assert.That(encodedBytes.Length, Is.EqualTo(consumedBytes));
            Assert.That(inputSummary.Method, Is.EqualTo(decodedSummary.Method));
            Assert.That(inputSummary.Url, Is.EqualTo(decodedSummary.Url));
            Assert.That(inputSummary.QueryString, Is.EqualTo(decodedSummary.QueryString));
            Assert.That(Array.Empty<byte>().SequenceEqual(decodedSummary.Body));
        }


        private static IEnumerable PostRequestSummaryWithBodyTestCases()
        {
            yield return new TestCaseData("Hello, world!", System.Text.Encoding.UTF8);
            yield return new TestCaseData("こんにちは、世界！", System.Text.Encoding.UTF8);
        }

        [TestCaseSource(nameof(PostRequestSummaryWithBodyTestCases))]
        public void EncodeAndDecode_PostRequestSummaryWithBody_SuccessfullyEncodesAndDecodes(string bodyText, System.Text.Encoding encoding)
        {
            // Arrange
            byte[] bodyBytes = encoding.GetBytes(bodyText);
            HttpRequestSummary inputSummary = new HttpRequestSummary
            {
                Method = "POST",
                Url = "/api/data",
                ContentType = "text/plain",
                Body = bodyBytes
            };
            byte[] encodedBytes;

            // Act
            bool encodeResult = SimpleHttpEncoder.TryEncodeHttpRequest(inputSummary, out encodedBytes);

            HttpRequestSummary decodedSummary;
            int consumedBytes = SimpleHttpEncoder.TryParseHttpRequest(encodedBytes, out decodedSummary);


            // Assert
            Assert.That(encodeResult, "Encoding failed");
            Assert.That(encodedBytes.Length, Is.EqualTo(consumedBytes), "Unexpected consumed bytes");
            Assert.That(inputSummary.Method, Is.EqualTo(decodedSummary.Method));
            Assert.That(inputSummary.Url, Is.EqualTo(decodedSummary.Url));
            Assert.That(inputSummary.QueryString, Is.EqualTo(decodedSummary.QueryString));
            Assert.That(inputSummary.Body.SequenceEqual(decodedSummary.Body));
        }
        // 
        // HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 15\r\n\r\n{"status":"OK"}


        [Test]
        public void TryParseHttpResponse_Valid200Response_ReturnsTrueAndParsesCorrectly()
        {
            // Arrange
            string request =
                "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 15\r\n\r\n{\"status\":\"OK\"}";
            byte[] requestData = System.Text.Encoding.UTF8.GetBytes(request);

            // Act
            int consumedBytes = SimpleHttpEncoder.TryParseHttpResponse(requestData, out HttpResponseSummary summary);

            // Assert
            Assert.That(consumedBytes > 0, Is.True);
            Assert.That(summary.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(summary.ContentType, Is.EqualTo("application/json"));
            Assert.That(System.Text.Encoding.UTF8.GetBytes("{\"status\":\"OK\"}").SequenceEqual(summary.Body));
        }

    }
}