using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Request.Body.Peeker
{
    /// <summary>
    /// Deserializes a peeked request body into an object, as an alternative to the built-in System.Text.Json support.
    /// </summary>
    public interface IBodySerializer
    {
        /// <summary>
        /// Deserializes the request body.
        /// </summary>
        /// <typeparam name="T">The type to deserialize the body into.</typeparam>
        /// <param name="utf8Body">
        /// The UTF-8 encoded request body. It is already fully buffered, so it may be read synchronously or
        /// asynchronously. It is owned by the caller and must not be disposed.
        /// </param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The deserialized value, or <see langword="null"/>/<see langword="default"/>.</returns>
        Task<T?> DeserializeAsync<T>(Stream utf8Body, CancellationToken cancellationToken);
    }
}
