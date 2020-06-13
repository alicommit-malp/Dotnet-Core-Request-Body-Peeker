# Read HttpContext.Request.Body, But not consuming it 
We are happy with .Net core's Middlewares and ActionFilters. We can have a moment with the HTTP request in the Middleware or ActionFilter and check the JWT validity or ApiKey with ease as far as they are in the HTTP header. As soon as we need to check a parameter in the request body we start facing some weird issues.

> A stream is like a one-time message, it will be gone, as soon as you read it.

The HttpContext.Request.Body is also a stream, by reading it in middleware 1 , you will end up with an empty stream in the MVC middleware if the pipeline's order is as following :

```
Middleware 1 -> MVC Middleware 
```

The solution is to read the stream and then put back in its place :) 

```c#
var request = HttpContext.Request;
request.EnableBuffering();
var buffer = new byte[Convert.ToInt32(request.ContentLength)];
request.Body.Read(buffer, 0, buffer.Length);
```
by enabling the buffering mode on the stream we can read the cloned version of the stream from the memory and after we have finished with reading we must set the stream position pointer to the beginning again like this

```c#
request.Body.Position = 0;
``` 

writing all above code, or having helper class in each project to take care of it can be annoying. That's why i have put together an extension to the HttpRequest class to take care of all these behind the scene. 

Install it like this
```bash
Install-Package Request.Body.Peeker -Version 1.0.0 
```

Use it like this to a get the request body as string
```c#
var request = context.HttpContext.Request.PeekBody();
```

Use it like this to a get the request body as your desired object type
```c#
LoginRequest request = context.HttpContext.Request.PeekBody<LoginReuqest>();
```
Or

```c#
LoginRequest request = await context.HttpContext.Request.PeekBodyAsync<LoginReuqest>();
```

Happy coding.


