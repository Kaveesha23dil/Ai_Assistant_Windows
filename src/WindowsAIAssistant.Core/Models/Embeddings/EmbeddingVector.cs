namespace WindowsAIAssistant.Core.Models.Embeddings;

/// <summary>
/// A vector of single-precision values, with the identity of the model that produced it.
/// <para>
/// The values are held as contiguous, read-only <see cref="float"/> data: contiguous so they
/// can be measured and compared without an array of boxed objects in the way, and read-only
/// because a vector something else could change while a search was comparing it would produce a
/// score corresponding to nothing. A hundred thousand chunks of 1,536 values is enormous as
/// boxed objects and about the same size as floats, and that difference is the whole reason a
/// desktop-scale knowledge base is possible at all.
/// </para>
/// <para>
/// The provider and model travel with the vector rather than living in a setting somewhere,
/// because they decide whether two vectors may be compared. That decision cannot be made by
/// whichever component happens to be holding one of them at the time.
/// </para>
/// </summary>
public sealed class EmbeddingVector
{
    private const float ZeroMagnitude = 1e-12f;

    private readonly float[] _values;
    private float _magnitude = -1f;

    /// <summary>
    /// Builds a vector, optionally scaling it to unit length on the way in.
    /// <para>
    /// Normalizing here rather than during every comparison is the point of the
    /// <see cref="IsNormalized"/> flag: a thousand stored vectors are each scaled once, on the
    /// way into the index, and a search then reduces to a dot product over values that are
    /// already unit length.
    /// </para>
    /// </summary>
    /// <param name="values">The values. Copied, so a later change to the caller's array cannot reach in.</param>
    /// <param name="model">The model identifier that produced the values.</param>
    /// <param name="provider">The provider that produced the values.</param>
    /// <param name="normalize">Whether to scale the vector to unit length on the way in.</param>
    public EmbeddingVector(
        ReadOnlySpan<float> values,
        string model,
        string provider,
        bool normalize = false)
        : this(values, model, provider, normalize, alreadyUnitLength: false)
    {
    }

    private EmbeddingVector(
        ReadOnlySpan<float> values,
        string model,
        string provider,
        bool normalize,
        bool alreadyUnitLength)
    {
        ArgumentOutOfRangeException.ThrowIfZero(values.Length);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);

        foreach (var value in values)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentException(
                    "An embedding value was not a finite number, so the vector cannot be compared.",
                    nameof(values));
            }
        }

        _values = values.ToArray();

        Model = model.Trim();
        Provider = provider.Trim();

        if (alreadyUnitLength)
        {
            _magnitude = 1f;
        }
        else if (normalize)
        {
            ScaleToUnitLength();
        }
    }

    /// <summary>Gets the values, in order.</summary>
    public ReadOnlyMemory<float> Values => _values;

    /// <summary>Gets how many values the vector holds.</summary>
    public int Dimensions => _values.Length;

    /// <summary>Gets the model identifier that produced the values.</summary>
    public string Model { get; }

    /// <summary>Gets the provider that produced the values.</summary>
    public string Provider { get; }

    /// <summary>
    /// Gets a value indicating whether the values are already scaled to unit length.
    /// <para>
    /// A vector of all zeros has no direction, so it is never treated as normalized however it
    /// was configured, and comparing it is refused rather than answered with a made-up score.
    /// </para>
    /// </summary>
    public bool IsNormalized => Math.Abs(Magnitude - 1f) < 1e-4f;

    /// <summary>Gets the space this vector belongs to.</summary>
    public EmbeddingSpace Space => new(Provider, Model, Dimensions);

    /// <summary>Gets one value, for a caller that needs to read a single component.</summary>
    /// <param name="index">The zero-based position to read.</param>
    public float this[int index] => _values[index];

    /// <summary>
    /// Gets the length of the vector, computed once and kept.
    /// <para>
    /// Cosine similarity needs it, and a search over a thousand vectors would otherwise take the
    /// square root of a million times. The cached value is why this is a field rather than a
    /// computed property.
    /// </para>
    /// </summary>
    public float Magnitude
    {
        get
        {
            if (_magnitude < 0f)
            {
                var sum = 0f;
                foreach (var value in _values)
                {
                    sum += value * value;
                }

                _magnitude = (float)Math.Sqrt(sum);
            }

            return _magnitude;
        }
    }

    /// <summary>Gets a value indicating whether the vector has a direction to be compared in.</summary>
    public bool HasMagnitude => Magnitude > ZeroMagnitude;

    /// <summary>
    /// Returns the same vector scaled to unit length, or itself when it already is, so that
    /// normalizing an already-normalized index costs nothing.
    /// </summary>
    public EmbeddingVector Normalize()
    {
        if (IsNormalized)
        {
            return this;
        }

        return new EmbeddingVector(_values, Model, Provider, normalize: true);
    }

    /// <summary>Copies the values out, for a caller that needs an array of its own.</summary>
    public float[] ToArray() => [.. _values];

    /// <summary>
    /// Rebuilds a vector from stored values.
    /// <para>
    /// Used when reading an index back. The model and provider come from the space the values
    /// were stored under rather than from anything the values themselves claim, so a round trip
    /// through the database cannot invent a mismatch that was not there.
    /// </para>
    /// </summary>
    /// <param name="values">The stored values.</param>
    /// <param name="space">The space they belong to.</param>
    /// <param name="normalized">Whether they were stored already scaled to unit length.</param>
    public static EmbeddingVector FromStored(
        ReadOnlySpan<float> values,
        EmbeddingSpace space,
        bool normalized)
    {
        ArgumentNullException.ThrowIfNull(space);

        if (values.Length != space.Dimensions)
        {
            throw new ArgumentException(
                $"The stored vector has {values.Length} values but its space says {space.Dimensions}.",
                nameof(values));
        }

        return new EmbeddingVector(values, space.Model, space.Provider, normalize: false, alreadyUnitLength: normalized);
    }

    /// <summary>
    /// Scales the values in place to unit length. Reached only from the constructor, which is
    /// what keeps a vector immutable from every other direction.
    /// </summary>
    private void ScaleToUnitLength()
    {
        var magnitude = Magnitude;
        if (magnitude <= ZeroMagnitude)
        {
            // Nothing to scale towards. Left as it is, and IsNormalized will say so, so a
            // caller comparing it is told the vector has no direction rather than being handed
            // a division by zero dressed up as a similarity.
            return;
        }

        for (var index = 0; index < _values.Length; index++)
        {
            _values[index] /= magnitude;
        }

        _magnitude = 1f;
    }
}
