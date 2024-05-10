# Peeking at HttpContext.Request.Body, without consuming it

## Usage

>Install it from [Nuget](https://www.nuget.org/packages/Request.Body.Peeker/)
>
>Check out the source code from [Github](https://github.com/alicommit-malp/Dotnet-Core-Request-Body-Peeker)

```bash
Install-Package Request.Body.Peeker
```

After installation you can read the HttpContext request body without consuming it as follows

```c#
//Return string
var request = context.HttpContext.Request.PeekBody();

//Return in expected type
LoginRequest request = context.HttpContext.Request.PeekBody<LoginRequest>();

//Return in expected type asynchronously
LoginRequest request = await context.HttpContext.Request.PeekBodyAsync<LoginRequest>();
```

We are happy with the .Net core's Middlewares and ActionFilters. They provide us with a moment with the HTTP request to check the JWT validity or ApiKey with ease but as far as the parameters which we are interested are located in the HTTP header or the query string. As soon as we need to check a value in the request body we start facing some weird issues.

> A stream is like a one-time message, it will be gone, as soon as you read it.

The HttpContext.Request.Body is also a stream, by reading it in middleware 1 , you will end up with an empty stream in the MVC middleware if the pipeline's order is as follows :

```bash
Middleware 1 -> MVC Middleware
```

> The known solution is to read the stream and then put back in its place.

```c#
var request = HttpContext.Request;
request.EnableBuffering();
var buffer = new byte[Convert.ToInt32(request.ContentLength)];
request.Body.Read(buffer, 0, buffer.Length);
```

By enabling the buffering mode on the HttpContext request body stream we can read the cloned version of the stream from the memory. After we have finished with the reading we must set the stream position pointer to the beginning again like this

```c#
request.Body.Position = 0;
```

Writing all above code, or having a helper class in each project to take care of it can be annoying. That's why i have put together a [Nuget](https://www.nuget.org/packages/Request.Body.Peeker/) extension to the HttpRequest class to take care of all this behind the scene.

>Install it from [Nuget](https://www.nuget.org/packages/Request.Body.Peeker/)
>
>Check out the source code from [Github](https://github.com/alicommit-malp/Dotnet-Core-Request-Body-Peeker)

## BenchMark
BenchmarkDotNet=v0.12.1, OS=macOS 11.2.3 (20D91) [Darwin 20.3.0]
- Intel Core i7-8850H CPU 2.60GHz (Coffee Lake), 1 CPU, 12 logical and 6 physical cores
.NET Core SDK=5.0.101
- [Host]     : .NET Core 5.0.1 (CoreCLR 5.0.120.57516, CoreFX 5.0.120.57516), X64 RyuJIT
- DefaultJob : .NET Core 5.0.1 (CoreCLR 5.0.120.57516, CoreFX 5.0.120.57516), X64 RyuJIT


|        Method |     Mean |    Error |   StdDev |   Median |  Gen 0 |  Gen 1 | Gen 2 | Allocated |
|-------------- |---------:|---------:|---------:|---------:|-------:|-------:|------:|----------:|
| PeekBodyAsync | 12.09 us | 0.766 us | 2.160 us | 11.25 us | 0.6714 | 0.1831 |     - |   3.63 KB |
|      PeekBody | 12.37 us | 0.556 us | 1.595 us | 12.14 us | 0.7019 | 0.1678 |     - |   3.63 KB |



Happy coding.
