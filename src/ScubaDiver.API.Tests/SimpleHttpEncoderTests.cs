using System.Collections;
using System.Collections.Specialized;
using System.Net;
using System.Net.Mime;
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
            Assert.AreEqual(true, consumedBytes > 0);
            Assert.AreEqual("GET", summary.Method);
            Assert.AreEqual("/path/to/resource", summary.Url);
            Assert.AreEqual(1, summary.QueryString.Count);
            Assert.AreEqual("value", summary.QueryString["param"]);
            CollectionAssert.AreEqual(new byte[] { 48, 49, 50, 51, 52, 53, 54, 55, 56, 57 }, summary.Body);
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
            Assert.AreEqual(true, consumedBytes > 0);
            Assert.AreEqual("GET", summary.Method);
            Assert.AreEqual("/path/to/resource", summary.Url);
            Assert.AreEqual(2, summary.QueryString.Count);
            Assert.AreEqual("value", summary.QueryString["param"]);
            Assert.AreEqual("lol", summary.QueryString["param2"]);
            CollectionAssert.AreEqual(new byte[] { 48, 49, 50, 51, 52, 53, 54, 55, 56, 57 }, summary.Body);
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
            Assert.AreEqual(true, consumedBytes > 0);
            Assert.AreEqual("POST", summary.Method);
            Assert.AreEqual("/api/data", summary.Url);
            Assert.AreEqual(0, summary.QueryString.Count);
            CollectionAssert.AreEqual(System.Text.Encoding.UTF8.GetBytes("Hello, world!"), summary.Body);
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
                Body = null
            };

            // Act
            byte[] encodedBytes;
            Assert.IsTrue(SimpleHttpEncoder.TryEncodeHttpRequest(inputSummary, out encodedBytes));

            HttpRequestSummary decodedSummary;
            int consumedBytes = SimpleHttpEncoder.TryParseHttpRequest(encodedBytes, out decodedSummary);

            // Assert
            Assert.AreEqual(encodedBytes.Length, consumedBytes);
            Assert.AreEqual(inputSummary.Method, decodedSummary.Method);
            Assert.AreEqual(inputSummary.Url, decodedSummary.Url);
            CollectionAssert.AreEqual(inputSummary.QueryString, decodedSummary.QueryString);
            CollectionAssert.AreEqual(Array.Empty<byte>(), decodedSummary.Body);
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
            Assert.IsTrue(encodeResult, "Encoding failed");
            Assert.AreEqual(encodedBytes.Length, consumedBytes, "Unexpected consumed bytes");
            Assert.AreEqual(inputSummary.Method, decodedSummary.Method);
            Assert.AreEqual(inputSummary.Url, decodedSummary.Url);
            Assert.AreEqual(inputSummary.QueryString, decodedSummary.QueryString);
            CollectionAssert.AreEqual(inputSummary.Body, decodedSummary.Body);
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
            Assert.AreEqual(true, consumedBytes > 0);
            Assert.AreEqual(HttpStatusCode.OK, summary.StatusCode);
            Assert.AreEqual("application/json", summary.ContentType);
            CollectionAssert.AreEqual(System.Text.Encoding.UTF8.GetBytes("{\"status\":\"OK\"}"), summary.Body);
        }

    }
}