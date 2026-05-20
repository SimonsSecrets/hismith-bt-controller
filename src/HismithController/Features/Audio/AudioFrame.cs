namespace HismithController.Audio;

public sealed record AudioFrame(
    float[] MonoSamples,
    int SampleRate,
    AudioSourceFormat SourceFormat,
    DateTimeOffset Timestamp);

public sealed record AudioSourceFormat(
    string Encoding,
    int SampleRate,
    int Channels,
    int BitsPerSample)
{
    public override string ToString() =>
        $"{Encoding}, {SampleRate} Hz, {Channels}ch, {BitsPerSample}-bit";
}
