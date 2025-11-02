using ScubaDiver.API.Interactions.Dumps;

namespace ScubaDiver.API.Interactions
{
    /// <summary>
    /// Response for a custom function registration request
    /// </summary>
    public class RegisterCustomFunctionResponse
    {
        /// <summary>
        /// Whether the registration was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if registration failed
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// The method dump for the registered function (when successful)
        /// </summary>
        public TypeDump.TypeMethod RegisteredMethod { get; set; }
    }
}
