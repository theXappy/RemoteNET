using ScubaDiver.API.Protocol.SimpleHttp;
using System.Text;

namespace ScubaDiver.API.Tests;

[TestFixture]
public class SimpleHttpProtocolParserTests
{
    [Test]
    public void ReadHttpRequestFromStream_ValidRequest_ParsesCorrectly()
    {
        // Arrange
        string httpRequest = "GET /example HTTP/1.1\r\nContent-Length: 13\r\n\r\nHello, World!";
        byte[] requestData = Encoding.ASCII.GetBytes(httpRequest);
        MemoryStream input = new MemoryStream(requestData);
        MemoryStream output = new MemoryStream();

        // Act
        SimpleHttpProtocolParser.ReadHttpMessageFromStream(input, output);

        // Assert
        output.Seek(0, SeekOrigin.Begin);
        byte[] outputData = output.ToArray();
        string parsedRequest = Encoding.ASCII.GetString(outputData);
        string expectedRequest = "GET /example HTTP/1.1\r\nContent-Length: 13\r\n\r\nHello, World!";

        Assert.AreEqual(expectedRequest, parsedRequest);
    }



    [Test]
    public void ReadHttpRequestFromStream_ValidRequestLeftOverData_ParsesCorrectly()
    {
        // Arrange
        string httpRequest = "GET /example HTTP/1.1\r\nContent-Length: 13\r\n\r\nHello, World!Get /lol";
        byte[] requestData = Encoding.ASCII.GetBytes(httpRequest);
        MemoryStream input = new MemoryStream(requestData);
        MemoryStream output = new MemoryStream();

        // Act
        SimpleHttpProtocolParser.ReadHttpMessageFromStream(input, output);

        // Assert
        output.Seek(0, SeekOrigin.Begin);
        byte[] outputData = output.ToArray();
        string parsedRequest = Encoding.ASCII.GetString(outputData);
        string expectedRequest = "GET /example HTTP/1.1\r\nContent-Length: 13\r\n\r\nHello, World!";

        Assert.AreEqual(expectedRequest, parsedRequest);
    }

    [Test]
    public void ReadHttpRequestFromStream_TwoRequests_ParsesCorrectly()
    {
        // Arrange
        string httpRequest1 = "GET /first HTTP/1.1\r\nContent-Length: 8\r\n\r\nRequest1";
        string httpRequest2 = "POST /second HTTP/1.1\r\nContent-Length: 8\r\n\r\nRequest2";
        string combinedRequests = httpRequest1 + httpRequest2;
        byte[] requestData = Encoding.ASCII.GetBytes(combinedRequests);
        MemoryStream input = new MemoryStream(requestData);
        MemoryStream output1 = new MemoryStream();
        MemoryStream output2 = new MemoryStream();

        // Act
        SimpleHttpProtocolParser.ReadHttpMessageFromStream(input, output1);
        SimpleHttpProtocolParser.ReadHttpMessageFromStream(input, output2);

        // Assert Request 1
        output1.Seek(0, SeekOrigin.Begin);
        byte[] outputData = output1.ToArray();
        string parsedRequest1 = Encoding.ASCII.GetString(outputData);
        string expectedRequest1 = "GET /first HTTP/1.1\r\nContent-Length: 8\r\n\r\nRequest1";
        Assert.AreEqual(expectedRequest1, parsedRequest1);

        // Assert Request 2
        output2.Seek(0, SeekOrigin.Begin);
        outputData = output2.ToArray();
        string parsedRequest2 = Encoding.ASCII.GetString(outputData);
        string expectedRequest2 = "POST /second HTTP/1.1\r\nContent-Length: 8\r\n\r\nRequest2";
        Assert.AreEqual(expectedRequest2, parsedRequest2);
    }

    [Test]
    public void ReadHttpRequestFromStream_PartialRequestHeader_DoesNotParse()
    {
        // Arrange
        string partialRequest = "GET /partial HTTP/1.1\r\nContent-Length: 7\r\n\r\nPartial";
        byte[] requestData = Encoding.ASCII.GetBytes(partialRequest);
        MemoryStream input = new MemoryStream(requestData, 0, 15); // Simulate partial data -- cutting off in the middle of the header
        MemoryStream output = new MemoryStream();

        // Act
        Assert.Throws<IOException>(() => SimpleHttpProtocolParser.ReadHttpMessageFromStream(input, output));
    }


    [Test]
    public void ReadHttpRequestFromStream_PartialRequestBody_DoesNotParse()
    {
        // Arrange
        string partialRequest = "GET /partial HTTP/1.1\r\nContent-Length: 100\r\n\r\nPartial"; // Simulate partial data -- content-length too big for the body
        byte[] requestData = Encoding.ASCII.GetBytes(partialRequest);
        MemoryStream input = new MemoryStream(requestData, 0, requestData.Length); 
        MemoryStream output = new MemoryStream();

        // Act
        Assert.Throws<IOException>(() => SimpleHttpProtocolParser.ReadHttpMessageFromStream(input, output));
    }
}