namespace EventPipelines {
    /// <summary>
    /// Subscribe contract for settled values — implement on any consumer the owner knows about,
    /// or use EventModified&lt;T&gt;.Settled / .OnSettle instead.
    /// </summary>
    public interface IEventListener<T> {
        public void OnEvent(T @event);
    }
}
