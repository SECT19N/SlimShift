using Xunit;

namespace SlimShift.Tests;

public class EncoderDetectorTests {
	const string Listing = """
		Encoders:
		 V..... = Video
		 ------
		 V....D libx264              libx264 H.264 / AVC (codec h264)
		 V....D h264_nvenc           NVIDIA NVENC H.264 encoder (codec h264)
		 A....D aac                  AAC (Advanced Audio Coding)
		 V.S... libsvtav1            SVT-AV1 (codec av1)
		""";

	[Fact]
	public void ParseEncoderNames_KeepsOnlyVideoEncoders() {
		HashSet<string> names = EncoderDetector.ParseEncoderNames(Listing);
		Assert.Contains("libx264", names);
		Assert.Contains("h264_nvenc", names);
		Assert.Contains("libsvtav1", names);
		Assert.DoesNotContain("aac", names);
		Assert.DoesNotContain("V.....", names);
	}
}

public class EncoderProfilesTests {
	static EncoderInfo Get(string name) => EncoderCatalog.All.Single(e => e.Name == name);

	[Theory]
	[InlineData("libx264", "-crf 23")]
	[InlineData("libx265", "-crf 23")]
	[InlineData("libvpx-vp9", "-crf 23")]
	[InlineData("libsvtav1", "-crf 23")]
	[InlineData("h264_nvenc", "-cq 23")]
	[InlineData("hevc_qsv", "-global_quality 23")]
	[InlineData("h264_amf", "-qp_i 23 -qp_p 23")]
	public void BuildVideoArgs_UsesTheRightQualityOption(string encoder, string expected) {
		string args = string.Join(' ', EncoderProfiles.BuildVideoArgs(Get(encoder), "medium", 23));
		Assert.Contains(expected, args);
		Assert.StartsWith($"-c:v {encoder}", args);
	}

	[Fact]
	public void BuildVideoArgs_GpuEncodersDoNotGetCrf() {
		foreach (string name in new[] { "h264_nvenc", "hevc_qsv", "h264_amf" })
			Assert.DoesNotContain("-crf", EncoderProfiles.BuildVideoArgs(Get(name), "medium", 23));
	}

	[Fact]
	public void BuildVideoArgs_Av1AmfScalesQpToItsWiderRange() {
		string args = string.Join(' ', EncoderProfiles.BuildVideoArgs(Get("av1_amf"), "medium", 30));
		Assert.Contains("-qp_i 120", args);
	}

	[Fact]
	public void BuildVideoArgs_SvtAv1MapsPresetToNumber() {
		Assert.Contains("-preset 4", string.Join(' ', EncoderProfiles.BuildVideoArgs(Get("libsvtav1"), "slow", 30)));
	}

	[Fact]
	public void BuildVideoArgs_HevcGetsHvc1TagForMp4() {
		Assert.Contains("hvc1", EncoderProfiles.BuildVideoArgs(Get("hevc_qsv"), "medium", 28));
		Assert.Contains("hvc1", EncoderProfiles.BuildVideoArgs(Get("libx265"), "medium", 28));
	}

	[Theory]
	[InlineData("libx264", 0, 51)]
	[InlineData("libvpx-vp9", 0, 63)]
	[InlineData("libsvtav1", 0, 63)]
	[InlineData("h264_nvenc", 1, 51)]
	[InlineData("av1_nvenc", 1, 63)]
	public void GetQualityRange_MatchesEncoder(string encoder, int min, int max) {
		QualityRange range = EncoderProfiles.GetQualityRange(Get(encoder));
		Assert.Equal(min, range.Min);
		Assert.Equal(max, range.Max);
		Assert.InRange(range.Default, range.Min, range.Max);
	}

	[Fact]
	public void GetPresets_EmptyWhereUnsupported() {
		Assert.Empty(EncoderProfiles.GetPresets(Get("libvpx-vp9")));
		Assert.Empty(EncoderProfiles.GetPresets(Get("libaom-av1")));
		Assert.Contains("veryslow", EncoderProfiles.GetPresets(Get("libx264")));
	}

	[Theory]
	[InlineData(".mp4", "aac", "-c:a copy")]
	[InlineData(".mp4", "flac", "-c:a aac")]
	[InlineData(".webm", "aac", "-c:a libopus")]
	[InlineData(".webm", "opus", "-c:a copy")]
	[InlineData(".mkv", "flac", "-c:a copy")]
	public void BuildAudioArgs_CopiesOnlyWhenContainerAllows(string ext, string codec, string expected) {
		string args = string.Join(' ', EncoderProfiles.BuildAudioArgs(ext, [codec]));
		Assert.StartsWith(expected, args);
	}

	[Fact]
	public void BuildAudioArgs_NoAudioMeansNoAudioFlag() =>
		Assert.Equal(["-an"], EncoderProfiles.BuildAudioArgs(".mp4", []));
}

public class EncoderRecommenderTests {
	static EncoderInfo Get(string name) => EncoderCatalog.All.Single(e => e.Name == name);

	[Fact]
	public void Recommend_PrefersHardwareByDefault() {
		EncoderInfo[] available = [Get("libx264"), Get("h264_qsv"), Get("h264_nvenc")];
		Recommendation? rec = EncoderRecommender.Recommend(available, VideoCodec.H264);
		Assert.Equal("h264_nvenc", rec?.Encoder.Name);
	}

	[Fact]
	public void Recommend_PreferSizePicksSoftware() {
		EncoderInfo[] available = [Get("libx264"), Get("h264_nvenc")];
		Assert.Equal("libx264", EncoderRecommender.Recommend(available, VideoCodec.H264, preferSize: true)?.Encoder.Name);
	}

	[Fact]
	public void Rank_PutsSvtAv1AheadOfLibaom() {
		EncoderInfo[] available = [Get("libaom-av1"), Get("libsvtav1")];
		Assert.Equal(["libsvtav1", "libaom-av1"], EncoderRecommender.Rank(available, VideoCodec.AV1).Select(e => e.Name));
	}

	[Fact]
	public void Recommend_NullWhenNoEncoderForCodec() =>
		Assert.Null(EncoderRecommender.Recommend([Get("libx264")], VideoCodec.AV1));
}

public class JobPlannerTests {
	static EncoderInfo Get(string name) => EncoderCatalog.All.Single(e => e.Name == name);
	static readonly VideoInfo Source = new(1920, 1080, 30, TimeSpan.FromSeconds(10), "h264", ["aac"]);
	static readonly EncoderInfo[] Available = [Get("libx264"), Get("libx265"), Get("libsvtav1")];

	[Fact]
	public void Plan_RefusesEncoderThatIsNotAvailable() {
		PlanResult result = JobPlanner.Plan(new EncodeRequest("in.mp4", EncoderName: "h264_nvenc"), Source, Available);
		Assert.False(result.Succeeded);
		Assert.Contains("not available", result.Error);
	}

	[Fact]
	public void Plan_DefaultsToSourceCodec() {
		PlanResult result = JobPlanner.Plan(new EncodeRequest("in.mp4"), Source, Available);
		Assert.Equal("libx264", result.Job?.Encoder.Name);
		Assert.EndsWith("in-slim.mp4", result.Job?.OutputPath);
	}

	[Fact]
	public void Plan_RejectsQualityOutsideRange() {
		PlanResult result = JobPlanner.Plan(new EncodeRequest("in.mp4", Quality: 60), Source, Available);
		Assert.False(result.Succeeded);
	}

	[Fact]
	public void Plan_AcceptsWideQualityForAv1() {
		PlanResult result = JobPlanner.Plan(new EncodeRequest("in.mp4", Codec: VideoCodec.AV1, Quality: 60), Source, Available);
		Assert.True(result.Succeeded);
	}

	[Fact]
	public void Plan_RejectsOddHeightAndWebmWithH264AndSameFile() {
		Assert.False(JobPlanner.Plan(new EncodeRequest("in.mp4", Height: 721), Source, Available).Succeeded);
		Assert.False(JobPlanner.Plan(new EncodeRequest("in.mp4", Output: "out.webm"), Source, Available).Succeeded);
		Assert.False(JobPlanner.Plan(new EncodeRequest("in.mp4", Output: "in.mp4"), Source, Available).Succeeded);
	}

	[Fact]
	public void Plan_RejectsPresetForEncoderWithoutPresets() {
		EncoderInfo[] vp9 = [Get("libvpx-vp9")];
		PlanResult result = JobPlanner.Plan(new EncodeRequest("in.mp4", Codec: VideoCodec.VP9, Preset: "slow"), Source, vp9);
		Assert.False(result.Succeeded);
	}
}

public class ConversionRunnerTests {
	static EncoderInfo Get(string name) => EncoderCatalog.All.Single(e => e.Name == name);
	static readonly VideoInfo Source = new(1920, 1080, 30, TimeSpan.FromSeconds(10), "h264", ["aac"]);

	[Fact]
	public void BuildArguments_IncludesFiltersInOrderAndKeepsPathsAsOneToken() {
		ConversionJob job = new("my [file].mp4", "out dir/out.mp4", Get("libx264"), 23, "medium", 720, 24, Interpolate: false);
		List<string> args = ConversionRunner.BuildArguments(job, Source);

		Assert.Contains("my [file].mp4", args);
		Assert.Equal("scale=-2:720,fps=24", args[args.IndexOf("-vf") + 1]);
		Assert.Equal("out dir/out.mp4", args[^1]);
	}

	[Fact]
	public void BuildArguments_UsesMinterpolateWhenAsked() {
		ConversionJob job = new("in.mp4", "out.mp4", Get("libx264"), 23, "medium", null, 60, Interpolate: true);
		List<string> args = ConversionRunner.BuildArguments(job, Source);
		Assert.Equal("minterpolate=fps=60", args[args.IndexOf("-vf") + 1]);
	}

	[Fact]
	public void BuildArguments_NoFilterWhenNothingToChange() {
		ConversionJob job = new("in.mp4", "out.mp4", Get("libx264"), 23, "medium", null, null, false);
		Assert.DoesNotContain("-vf", ConversionRunner.BuildArguments(job, Source));
	}

	[Theory]
	[InlineData("out_time_us=1500000", true, 1.5)]
	[InlineData("out_time_ms=2000000", true, 2.0)]
	[InlineData("out_time_us=N/A", false, 0)]
	[InlineData("frame=100", false, 0)]
	public void TryParseProgressSeconds_ParsesFfmpegProgressLines(string line, bool ok, double seconds) {
		Assert.Equal(ok, ConversionRunner.TryParseProgressSeconds(line, out double value));
		Assert.Equal(seconds, value, 3);
	}
}

public class VideoProbeTests {
	const string Json = """
		{
		  "streams": [
		    { "codec_type": "video", "codec_name": "mjpeg", "width": 100, "height": 100,
		      "avg_frame_rate": "0/0", "disposition": { "attached_pic": 1 } },
		    { "codec_type": "video", "codec_name": "hevc", "width": 3840, "height": 2160,
		      "avg_frame_rate": "30000/1001", "disposition": { "attached_pic": 0 } },
		    { "codec_type": "audio", "codec_name": "aac" },
		    { "codec_type": "audio", "codec_name": "opus" }
		  ],
		  "format": { "duration": "12.5" }
		}
		""";

	[Fact]
	public void ParseJson_ReadsMainVideoStreamAndSkipsCoverArt() {
		VideoInfo info = VideoProbe.ParseJson(Json);
		Assert.Equal(3840, info.Width);
		Assert.Equal(2160, info.Height);
		Assert.Equal(29.97, info.Fps, 2);
		Assert.Equal(TimeSpan.FromSeconds(12.5), info.Duration);
		Assert.Equal(VideoCodec.H265, info.Codec);
		Assert.Equal(["aac", "opus"], info.AudioCodecs);
	}

	[Fact]
	public void ParseJson_ThrowsWithoutVideo() =>
		Assert.Throws<InvalidOperationException>(() => VideoProbe.ParseJson("""{ "streams": [] }"""));

	[Theory]
	[InlineData("25", 25)]
	[InlineData("24000/1001", 23.976)]
	[InlineData("0/0", 0)]
	[InlineData(null, 0)]
	public void ParseRate_HandlesFractionsAndZero(string? rate, double expected) =>
		Assert.Equal(expected, VideoProbe.ParseRate(rate), 3);
}
