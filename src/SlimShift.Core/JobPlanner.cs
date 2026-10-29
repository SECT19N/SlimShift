namespace SlimShift;

/// <summary>What the user asked for. Anything left null is chosen automatically.</summary>
public sealed record EncodeRequest(
	string Input,
	string? Output = null,
	VideoCodec? Codec = null,
	string? EncoderName = null,
	int? Quality = null,
	string? Preset = null,
	bool PreferSize = false,
	int? Height = null,
	double? Fps = null,
	bool Interpolate = false);

/// <summary>A fully resolved, validated encode.</summary>
public sealed record ConversionJob(
	string InputPath,
	string OutputPath,
	EncoderInfo Encoder,
	int Quality,
	string? Preset,
	int? TargetHeight,
	double? TargetFps,
	bool Interpolate);

public sealed record PlanResult(ConversionJob? Job, string? Error, Recommendation? Recommendation = null) {
	public bool Succeeded => Job != null;
}

/// <summary>Turns a request into a job, refusing anything this machine cannot do.</summary>
public static class JobPlanner {
	public static PlanResult Plan(EncodeRequest request, VideoInfo source, IReadOnlyList<EncoderInfo> available) {
		EncoderInfo encoder;
		Recommendation? recommendation = null;

		if (request.EncoderName != null) {
			if (EncoderCatalog.All.All(e => e.Name != request.EncoderName))
				return Fail($"'{request.EncoderName}' is not an encoder SlimShift supports.");

			EncoderInfo? chosen = available.FirstOrDefault(e => e.Name == request.EncoderName);
			if (chosen == null) {
				string names = available.Count == 0 ? "none" : string.Join(", ", available.Select(e => e.Name));
				return Fail($"Encoder '{request.EncoderName}' is not available on this machine. Available: {names}.");
			}
			if (request.Codec != null && request.Codec != chosen.Codec)
				return Fail($"Encoder '{chosen.Name}' produces {chosen.Codec.DisplayName()}, not {request.Codec.Value.DisplayName()}.");
			encoder = chosen;
		} else {
			VideoCodec codec = request.Codec ?? source.Codec ?? VideoCodec.H264;
			recommendation = EncoderRecommender.Recommend(available, codec, request.PreferSize);
			if (recommendation == null)
				return Fail($"No working {codec.DisplayName()} encoder was found on this machine.");
			encoder = recommendation.Encoder;
		}

		QualityRange range = EncoderProfiles.GetQualityRange(encoder);
		int quality = request.Quality ?? range.Default;
		if (quality < range.Min || quality > range.Max)
			return Fail($"Quality for {encoder.Name} must be between {range.Min} and {range.Max}.");

		IReadOnlyList<string> presets = EncoderProfiles.GetPresets(encoder);
		string? preset = request.Preset ?? EncoderProfiles.GetDefaultPreset(encoder);
		if (request.Preset != null) {
			if (presets.Count == 0)
				return Fail($"{encoder.Name} does not support presets.");
			if (!presets.Contains(request.Preset))
				return Fail($"Preset for {encoder.Name} must be one of: {string.Join(", ", presets)}.");
		}

		if (request.Height is { } h && (h <= 0 || h % 2 != 0))
			return Fail("Height must be a positive, even number of pixels.");
		if (request.Fps is <= 0)
			return Fail("Framerate must be greater than zero.");

		string output = request.Output ?? DefaultOutputPath(request.Input, encoder.Codec);
		string extension = Path.GetExtension(output).ToLowerInvariant();
		if (extension == ".webm" && encoder.Codec is not (VideoCodec.VP9 or VideoCodec.AV1))
			return Fail($".webm only holds VP9 or AV1 video, not {encoder.Codec.DisplayName()}.");
		if (extension.Length == 0)
			return Fail("The output file needs an extension such as .mp4, .mkv or .webm.");
		if (Path.GetFullPath(output).Equals(Path.GetFullPath(request.Input), StringComparison.OrdinalIgnoreCase))
			return Fail("The output file would overwrite the input file.");

		return new PlanResult(
			new ConversionJob(request.Input, output, encoder, quality, preset, request.Height, request.Fps, request.Interpolate),
			null, recommendation);
	}

	/// <summary>"movie.mkv" becomes "movie-slim.mp4" (extension chosen for the codec) beside the input.</summary>
	public static string DefaultOutputPath(string input, VideoCodec codec) {
		string directory = Path.GetDirectoryName(Path.GetFullPath(input)) ?? ".";
		string name = Path.GetFileNameWithoutExtension(input) + "-slim" + EncoderProfiles.DefaultExtension(codec);
		return Path.Combine(directory, name);
	}

	static PlanResult Fail(string error) => new(null, error);
}
