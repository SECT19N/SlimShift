namespace SlimShift;

public sealed record Recommendation(EncoderInfo Encoder, string Reason);

/// <summary>Ranks the encoders that work on this machine and suggests the best one.</summary>
public static class EncoderRecommender {
	// Hardware vendors in order of general preference when speed is the goal.
	static readonly EncoderVendor[] HardwareOrder =
		[EncoderVendor.Nvidia, EncoderVendor.Apple, EncoderVendor.Intel, EncoderVendor.Amd];

	/// <summary>
	/// Best first. By default hardware encoders lead (fast); with <paramref name="preferSize"/> software
	/// encoders lead (slower, but smaller files at the same quality).
	/// </summary>
	public static IReadOnlyList<EncoderInfo> Rank(
		IEnumerable<EncoderInfo> available, VideoCodec codec, bool preferSize = false) {
		List<EncoderInfo> forCodec = available.Where(e => e.Codec == codec).ToList();

		// Catalog order already puts libsvtav1 ahead of libaom-av1.
		List<EncoderInfo> software = forCodec.Where(e => !e.IsHardware)
			.OrderBy(e => EncoderCatalog.All.ToList().FindIndex(c => c.Name == e.Name)).ToList();
		List<EncoderInfo> hardware = forCodec.Where(e => e.IsHardware)
			.OrderBy(e => Array.IndexOf(HardwareOrder, e.Vendor)).ToList();

		return preferSize ? software.Concat(hardware).ToList() : hardware.Concat(software).ToList();
	}

	public static Recommendation? Recommend(
		IEnumerable<EncoderInfo> available, VideoCodec codec, bool preferSize = false) {
		IReadOnlyList<EncoderInfo> ranked = Rank(available, codec, preferSize);
		if (ranked.Count == 0) return null;

		EncoderInfo best = ranked[0];
		string reason = (best.IsHardware, preferSize) switch {
			(true, _) => $"{best.Vendor} hardware encoding is much faster than software",
			(false, true) => "software encoding gives the smallest files at a given quality",
			(false, false) => "no working hardware encoder was found for this codec"
		};
		return new Recommendation(best, reason);
	}
}
