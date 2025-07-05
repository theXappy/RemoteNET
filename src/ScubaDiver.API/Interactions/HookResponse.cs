namespace ScubaDiver.API.Interactions
{
    public class HookResponse 
    {
        /// <summary>
        /// Only applicable for "pre" hooks
        /// </summary>
        public bool SkipOriginal { get; set; }

        /// <summary>
        /// Value to return from the hook.
        /// For "Pre" hooks, this value will only be used if <see cref="SkipOriginal"/> is true.
        /// For "Post" hooks, this value will be used regardless. If not explicitly set by the user, the original return value is expected here.
        /// </summary>
        public ObjectOrRemoteAddress? ReturnValue { get; set; }
    }
}