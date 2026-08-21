// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Fresco.Brix.Engrave;
using Fresco.Brix.Services;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using CodeBrix.LilyPort;

namespace Fresco.Brix.Core.Tests;

/// <summary>A job that finishes when the test says so, and says what it likes.</summary>
internal sealed class FakeJob : EngraveJob
{
    private readonly TaskCompletionSource<bool> _finish
        = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    public FakeJob(string title = "fake")
        : base(title)
    {
    }

    public void Complete(bool success) => _finish.TrySetResult(success);

    protected override async Task<bool> RunAsync()
    {
        using (CancellationToken.Register(() => _finish.TrySetCanceled()))
        {
            return await _finish.Task;
        }
    }
}

/// <summary>The message stream, the history and the ending of a job.</summary>
public class EngraveJobTests
{
    [Fact]
    public async Task a_job_reports_start_and_success()
    {
        //Arrange
        FakeJob job = new FakeJob("engrave");

        //Act
        Task run = job.StartAsync();
        job.Complete(true);
        await run;

        //Assert
        job.Success.Should().BeTrue();
        job.IsRunning.Should().BeFalse();
        job.History(MessageType.Neutral).Should().ContainSingle();
        job.History(MessageType.Success).Should().ContainSingle();
    }

    [Fact]
    public async Task a_failed_job_reports_a_failure_message()
    {
        //Arrange
        FakeJob job = new FakeJob();

        //Act
        Task run = job.StartAsync();
        job.Complete(false);
        await run;

        //Assert
        job.Success.Should().BeFalse();
        job.History(MessageType.Failure).Should().ContainSingle();
    }

    [Fact]
    public async Task an_aborted_job_says_so_and_is_not_a_failure_message()
    {
        //Arrange
        FakeJob job = new FakeJob("engrave");

        //Act
        Task run = job.StartAsync();
        job.Abort();
        await run;

        //Assert
        job.IsAborted.Should().BeTrue();
        job.Success.Should().BeFalse();

        //The abort message is neutral; nothing FAILED, the user changed their mind.
        job.History(MessageType.Failure).Should().BeEmpty();
        job.History(MessageType.Neutral).Should().HaveCount(2);
    }

    [Fact]
    public async Task history_is_replayable_and_filtered_by_channel()
    {
        //Arrange
        FakeJob job = new FakeJob();
        Task run = job.StartAsync();

        //Act
        job.Message("warning: something\n", MessageType.StdErr);
        job.Message("progress\n", MessageType.StdOut);
        job.Complete(true);
        await run;

        //Assert
        job.StdErr().Should().Be("warning: something\n");
        job.StdOut().Should().Be("progress\n");
        job.History().Count.Should().BeGreaterThan(2);
    }

    [Fact]
    public void elapsed_time_is_formatted_the_short_way()
    {
        //Assert
        EngraveJob.ElapsedToString(TimeSpan.FromSeconds(2.34)).Should().Be("2.3\"");
        EngraveJob.ElapsedToString(TimeSpan.FromSeconds(125)).Should().Be("2'5\"");
    }
}

/// <summary>The queue's ordering, its slots and its states.</summary>
public class JobQueueTests
{
    [Fact]
    public void a_priority_store_serves_the_lowest_priority_first()
    {
        //Arrange
        PriorityJobStore store = new PriorityJobStore();
        FakeJob engrave = new FakeJob("engrave") { Priority = 2 };
        FakeJob crawl = new FakeJob("crawl") { Priority = 1 };

        //Act
        store.Push(engrave);
        store.Push(crawl);

        //Assert
        store.Pop().Should().BeSameAs(crawl);
        store.Pop().Should().BeSameAs(engrave);
    }

    [Fact]
    public void equal_priorities_keep_their_order()
    {
        //Arrange
        PriorityJobStore store = new PriorityJobStore();
        FakeJob first = new FakeJob("first");
        FakeJob second = new FakeJob("second");

        //Act
        store.Push(first);
        store.Push(second);

        //Assert
        store.Pop().Should().BeSameAs(first);
        store.Pop().Should().BeSameAs(second);
    }

    [Fact]
    public void a_fifo_store_serves_in_arrival_order()
    {
        //Arrange
        FifoJobStore store = new FifoJobStore();
        FakeJob first = new FakeJob("first");
        FakeJob second = new FakeJob("second");

        //Act
        store.Push(first);
        store.Push(second);

        //Assert
        store.Pop().Should().BeSameAs(first);
    }

    [Fact]
    public void a_stack_store_serves_the_newest_first()
    {
        //Arrange
        StackJobStore store = new StackJobStore();
        FakeJob first = new FakeJob("first");
        FakeJob second = new FakeJob("second");

        //Act
        store.Push(first);
        store.Push(second);

        //Assert
        store.Pop().Should().BeSameAs(second);
    }

    [Fact]
    public async Task one_slot_runs_the_queued_jobs_in_sequence()
    {
        //Arrange
        JobQueue queue = new JobQueue();
        FakeJob first = new FakeJob("first");
        FakeJob second = new FakeJob("second");
        List<string> finished = new List<string>();
        queue.JobDone += (_, job) => finished.Add(job.Title);

        //Act
        queue.AddJob(first);
        queue.AddJob(second);
        bool secondWaited = !second.HasStarted;
        first.Complete(true);
        await WaitUntil(() => second.IsRunning);
        second.Complete(true);
        await WaitUntil(() => finished.Count == 2);

        //Assert
        secondWaited.Should().BeTrue();
        finished.Should().Equal("first", "second");
        queue.Completed().Should().Be(2);
    }

    [Fact]
    public async Task a_continuous_queue_goes_idle_rather_than_finishing()
    {
        //Arrange
        JobQueue queue = new JobQueue();
        FakeJob job = new FakeJob();

        //Act
        queue.AddJob(job);
        job.Complete(true);
        await WaitUntil(() => queue.State == QueueStatus.Idle);

        //Assert
        queue.State.Should().Be(QueueStatus.Idle);
        queue.IsLive.Should().BeTrue();
    }

    [Fact]
    public void an_aborted_queue_refuses_new_jobs()
    {
        //Arrange
        JobQueue queue = new JobQueue();

        //Act
        queue.Abort();

        //Assert
        Assert.Throws<JobQueueStateException>(() => queue.AddJob(new FakeJob()));
    }

    [Fact]
    public void a_paused_queue_holds_what_it_is_given()
    {
        //Arrange
        JobQueue queue = new JobQueue();

        //Act
        queue.Pause();
        queue.AddJob(new FakeJob());

        //Assert
        queue.Size.Should().Be(1);
        queue.IsIdle.Should().BeTrue();
    }

    [Fact]
    public void the_global_queue_knows_its_three_targets()
    {
        //Arrange
        GlobalJobQueue queues = new GlobalJobQueue();

        //Assert
        queues.Queue("engrave").Should().NotBeNull();
        queues.Queue("crawl").Should().NotBeNull();
        queues.Queue("generic").Should().NotBeNull();
        Assert.Throws<ArgumentException>(() => queues.Queue("nonexistent"));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}

/// <summary>One job at a time per document, and the announcements.</summary>
public class JobManagerTests
{
    [Fact]
    public async Task a_second_job_is_refused_while_one_runs()
    {
        //Arrange
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.CreateDocument();
        JobManager manager = JobManager.For(document);
        FakeJob first = new FakeJob("first");
        FakeJob second = new FakeJob("second");

        //Act
        manager.StartJob(first);
        manager.StartJob(second);

        //Assert
        manager.Job.Should().BeSameAs(first);
        second.HasStarted.Should().BeFalse();

        first.Complete(true);
        await WaitFor(() => !manager.IsRunning);
    }

    [Fact]
    public async Task the_application_hears_about_every_job()
    {
        //Arrange
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.CreateDocument();
        List<string> heard = new List<string>();

        void Started(object sender, JobEventArgs e) => heard.Add("started");
        void Finished(object sender, JobEventArgs e)
            => heard.Add(e.Success ? "succeeded" : "failed");

        JobManager.AnyJobStarted += Started;
        JobManager.AnyJobFinished += Finished;
        try
        {
            FakeJob job = new FakeJob();

            //Act
            JobManager.For(document).StartJob(job);
            job.Complete(true);
            await WaitFor(() => heard.Count == 2);
        }
        finally
        {
            JobManager.AnyJobStarted -= Started;
            JobManager.AnyJobFinished -= Finished;
        }

        //Assert
        heard.Should().Equal("started", "succeeded");
    }

    [Fact]
    public void a_hidden_job_is_marked_beside_the_job_not_on_it()
    {
        //Arrange
        FakeJob job = new FakeJob();

        //Act
        JobAttributes.For(job).Hidden = true;

        //Assert
        JobAttributes.For(job).Hidden.Should().BeTrue();
        JobAttributes.For(job).Job.Should().BeSameAs(job);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}

/// <summary>Where a run happens and what it is expected to leave behind.</summary>
public class DocumentInfoTests
{
    [Fact]
    public void a_saved_unmodified_document_is_engraved_where_it_lives()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        string path = folder.File("score.ly", "\\version \"" + LilyPortInfo.CompatibleWithVersion + "\"\n{ c'4 }\n");
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.OpenDocument(path);

        //Act
        var info = DocumentInfo.For(document).JobInfo();

        //Assert
        info.FileName.Should().Be(path);
    }

    [Fact]
    public void a_modified_document_is_engraved_from_a_scratch_copy()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        string path = folder.File("score.ly", "{ c'4 }\n");
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.OpenDocument(path);
        document.Document.Text = "{ d'4 }\n";

        //Act
        var info = DocumentInfo.For(document).JobInfo(create: true);

        //Assert
        info.FileName.Should().NotBe(path);
        File.ReadAllText(info.FileName).Should().Be("{ d'4 }\n");

        //The document's own folder goes on the front of the include path, so
        //its relative includes still resolve from the scratch area.
        info.IncludePath.First().Should().Be(folder.Path);
    }

    [Fact]
    public void a_nameless_document_gets_a_scratch_name_with_the_right_extension()
    {
        //Arrange
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.CreateDocument();
        document.Document.Text = "\\version \"" + LilyPortInfo.CompatibleWithVersion + "\"\n{ c'4 }\n";

        //Act
        var info = DocumentInfo.For(document).JobInfo(create: true);

        //Assert
        Path.GetFileName(info.FileName).Should().Be("document.ly");
    }

    [Fact]
    public void the_base_names_include_what_bookOutputName_asks_for()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        string path = folder.File("score.ly",
            "\\book { \\bookOutputName \"parts\" { c'4 } }\n");
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.OpenDocument(path);

        //Act
        IReadOnlyList<string> names = DocumentInfo.For(document).BaseNames();

        //Assert
        names.Should().Contain(Path.Combine(folder.Path, "score"));
        names.Should().Contain(Path.Combine(folder.Path, "parts"));
    }

    [Fact]
    public void an_output_variable_replaces_the_base_names_outright()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        string path = folder.File("score.ly",
            "%% -*- output: one, two;\n{ c'4 }\n");
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.OpenDocument(path);

        //Act
        IReadOnlyList<string> names = DocumentInfo.For(document).BaseNames();

        //Assert
        names.Should().Equal(
            Path.Combine(folder.Path, "one"), Path.Combine(folder.Path, "two"));
    }

    [Fact]
    public void included_files_are_followed_recursively()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        folder.File("inner.ily", "{ c'4 }\n");
        folder.File("middle.ily", "\\include \"inner.ily\"\n");
        string path = folder.File("score.ly", "\\include \"middle.ily\"\n");
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.OpenDocument(path);

        //Act
        IReadOnlyCollection<string> files = DocumentInfo.For(document).IncludeFiles();

        //Assert
        files.Should().HaveCount(2);
        files.Should().Contain(Path.Combine(folder.Path, "inner.ily"));
        files.Should().Contain(Path.Combine(folder.Path, "middle.ily"));
    }

    [Fact]
    public void the_version_is_also_looked_for_in_the_variables()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        string path = folder.File("score.ly", "%% -*- version: 2.18.2;\n{ c'4 }\n");
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.OpenDocument(path);

        //Act
        string version = DocumentInfo.For(document).DocInfo().VersionString();

        //Assert
        version.Should().Be("2.18.2");
    }

    [Fact]
    public void an_output_suffix_is_sanitized_the_way_the_engine_sanitizes_it()
    {
        //Assert
        LyFileInfo.ReplaceSuffixChars("part one!").Should().Be("part_one_");
        LyFileInfo.ReplaceSuffixChars("violin-1").Should().Be("violin-1");
    }
}

/// <summary>What a run left on disk, and when those answers are frozen.</summary>
public class ResultFilesTests
{
    [Fact]
    public void the_output_of_a_run_is_found_beside_the_source()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        string path = folder.File("score.ly", "{ c'4 }\n");
        folder.File("score.svg", "<svg/>");
        folder.File("score-1.svg", "<svg/>");
        folder.File("score.midi", "MThd");
        folder.File("unrelated.svg", "<svg/>");
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.OpenDocument(path);

        //Act
        IReadOnlyList<string> svg = ResultFiles.For(document).Files(".svg", newer: false);
        IReadOnlyList<string> all = ResultFiles.For(document).Files("*", newer: false);

        //Assert
        svg.Select(Path.GetFileName).Should().Equal("score.svg", "score-1.svg");
        all.Select(Path.GetFileName).Should().Contain("score.midi");
        all.Select(Path.GetFileName).Should().NotContain("unrelated.svg");
    }

    [Fact]
    public void files_are_gathered_by_kind_in_the_order_the_menu_lists_them()
    {
        //Act
        IReadOnlyList<IReadOnlyList<string>> groups = PathUtil.GroupFiles(
            new[] { "a.ly", "a.midi", "a.svg", "a.pdf", "a.log" },
            new[] { "pdf", "mid midi", "svg svgz", "png", "!ly ily lyi" });

        //Assert
        groups[0].Should().Equal("a.pdf");
        groups[1].Should().Equal("a.midi");
        groups[2].Should().Equal("a.svg");
        groups[3].Should().BeEmpty();

        //The negated group takes everything that is not a source file — and
        //the .ly is claimed by nothing, which is the point of the "!".
        groups[4].Should().Equal("a.log");
    }

    [Fact]
    public void numbered_pages_sort_the_way_a_person_reads_them()
    {
        //Arrange
        List<string> pages = new List<string>
        {
            "score-10.svg", "score-2.svg", "score-1.svg",
        };

        //Act
        pages.Sort(PathUtil.CompareFileNames);

        //Assert
        pages.Should().Equal("score-1.svg", "score-2.svg", "score-10.svg");
    }
}

/// <summary>The file locations in engine messages, and what they point at.</summary>
public class EngraveErrorsTests
{
    [Fact]
    public void a_location_is_recognized_and_split_into_its_parts()
    {
        //Act
        var match = EngraveErrors.MessagePattern.Match(
            "/tmp/score.ly:12:5: error: unexpected '}'\n");

        //Assert
        match.Success.Should().BeTrue();
        match.Groups[1].Value.Should().Be("/tmp/score.ly:12:5");
        match.Groups[2].Value.Should().Be("/tmp/score.ly");
        match.Groups[3].Value.Should().Be("12");
        match.Groups[4].Value.Should().Be("5");
    }

    [Fact]
    public void a_location_without_a_column_is_still_a_location()
    {
        //Act
        var match = EngraveErrors.MessagePattern.Match("/tmp/score.ly:7: warning: hm\n");

        //Assert
        match.Success.Should().BeTrue();
        match.Groups[3].Value.Should().Be("7");
        match.Groups[4].Success.Should().BeFalse();
    }

    [Fact]
    public void prose_that_merely_contains_a_colon_is_not_a_location()
    {
        //Act
        var match = EngraveErrors.MessagePattern.Match("Processing `score.ly'\n");

        //Assert
        match.Success.Should().BeFalse();
    }

    [Fact]
    public async Task an_error_binds_to_the_open_document_and_survives_editing()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        string path = folder.File("score.ly", "line one\nline two\nline three\n");
        DocumentManager documents = new DocumentManager();
        EngraveErrors.Documents = documents;
        EditorDocument document = documents.OpenDocument(path);

        FakeJob job = new FakeJob();
        EngraveErrors errors = EngraveErrors.For(document);
        errors.ConnectJob(job);
        Task run = job.StartAsync();

        int lineThree = document.OffsetAtPosition(3, 1);

        //Act
        job.Message(path + ":3:1: error: nope\n", MessageType.StdErr);
        int before = errors.Reference(path + ":3:1").Offset.Value;

        //Insert a whole line above the error: the reported line 3 is now line 4,
        //but the anchor still points at the same characters.
        document.Document.Insert(0, "inserted\n");
        int after = errors.Reference(path + ":3:1").Offset.Value;

        job.Complete(false);
        await run;

        //Assert
        before.Should().Be(lineThree);
        after.Should().Be(before + "inserted\n".Length);
        errors.Reference(path + ":3:1").Document.Should().BeSameAs(document);
    }

    [Fact]
    public async Task an_error_in_a_scratch_copy_points_at_the_document_it_was_made_from()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        string path = folder.File("score.ly", "{ c'4 }\n");
        DocumentManager documents = new DocumentManager();
        EngraveErrors.Documents = documents;
        EditorDocument document = documents.OpenDocument(path);
        document.Document.Text = "{ c'4\n";
        string scratch = DocumentInfo.For(document).JobInfo(create: true).FileName;

        //The engine names the file as the PARSER saw it — the base name — so
        //the job's directory is what turns it back into a path.
        FakeJob job = new FakeJob { Directory = Path.GetDirectoryName(scratch) };
        EngraveErrors errors = EngraveErrors.For(document);
        errors.ConnectJob(job);
        Task run = job.StartAsync();

        //Act
        job.Message("score.ly:1:6: error: unexpected end of input\n", MessageType.StdErr);
        job.Complete(false);
        await run;

        //Assert
        errors.Reference("score.ly:1:6").Document.Should().BeSameAs(document);
    }

    [Fact]
    public async Task a_bare_file_name_resolves_against_the_directory_the_run_happened_in()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        string path = folder.File("score.ly", "one\ntwo\n");
        DocumentManager documents = new DocumentManager();
        EngraveErrors.Documents = documents;
        EditorDocument document = documents.OpenDocument(path);

        FakeJob job = new FakeJob { Directory = folder.Path };
        EngraveErrors errors = EngraveErrors.For(document);
        errors.ConnectJob(job);
        Task run = job.StartAsync();

        //The text store belongs to the thread that made it, and awaiting can
        //come back on another one — so what is compared is read up front.
        int lineTwo = document.OffsetAtPosition(2, 1);

        //Act
        job.Message("score.ly:2:1: error: nope\n", MessageType.StdErr);
        ErrorReference reference = errors.Reference("score.ly:2:1");
        job.Complete(false);
        await run;

        //Assert
        reference.FileName.Should().Be(PathUtil.NormPath(path));
        reference.Document.Should().BeSameAs(document);
        reference.Offset.Should().Be(lineTwo);
    }
}

/// <summary>The layout-control modes and the options they ask for.</summary>
public class LayoutControlTests
{
    [Fact]
    public void no_mode_chosen_means_an_ordinary_run()
    {
        //Act
        IReadOnlyList<string> options = LayoutControl.PreviewOptions(
            Array.Empty<string>());

        //Assert
        options.Should().Equal("-dpoint-and-click");
    }

    [Fact]
    public void a_chosen_mode_brings_the_formatter_files_with_it()
    {
        //Act
        IReadOnlyList<string> options = LayoutControl.PreviewOptions(new[] { "voices" });

        //Assert
        options[0].Should().Be("-dpoint-and-click");
        options[1].Should().StartWith("-I");
        options[2].Should().Be("-dinclude-settings=debug-layout-options.ly");
        options.Should().Contain("-ddebug-voices");
    }

    [Fact]
    public void the_modes_come_out_in_the_panel_order_not_the_asking_order()
    {
        //Act
        IReadOnlyList<string> options = LayoutControl.PreviewOptions(
            new[] { "annotate-spacing", "voices" });

        //Assert
        options.SkipWhile(o => !o.StartsWith("-ddebug", StringComparison.Ordinal))
            .Should().Equal("-ddebug-voices", "-ddebug-annotate-spacing");
    }

    [Fact]
    public void every_mode_has_a_label_and_an_explanation()
    {
        //Assert
        foreach (var mode in LayoutControl.ModeList)
        {
            LayoutControl.Label(mode).Should().NotBeNullOrEmpty();
            LayoutControl.ToolTip(mode).Should().NotBeNullOrEmpty();
            LayoutControl.Option(mode).Should().StartWith("-d");
        }
    }
}

/// <summary>How a job's options are read, written and reported.</summary>
public class LilyPondJobOptionTests
{
    [Fact]
    public void a_negated_option_parses_to_false()
    {
        //Assert
        LilyPondJob.ParseOption("-dno-point-and-click").Should()
            .Be(("point-and-click", (object)false));
        LilyPondJob.ParseOption("-dpoint-and-click").Should()
            .Be(("point-and-click", (object)true));
    }

    [Fact]
    public void an_option_with_a_value_keeps_its_value()
    {
        //Assert
        LilyPondJob.ParseOption("-dresolution=300").Should()
            .Be(("resolution", (object)"300"));
    }

    [Fact]
    public void options_serialize_back_to_the_form_they_came_in()
    {
        //Arrange
        Dictionary<string, object> options = new Dictionary<string, object>
        {
            ["point-and-click"] = false,
            ["preview"] = true,
            ["resolution"] = "300",
        };

        //Act
        IReadOnlyList<string> tokens = LilyPondJob.SerializeOptions(options, ordered: true);

        //Assert
        tokens.Should().Equal(
            "-dno-point-and-click", "-dpreview", "-dresolution=300");
    }

    [Fact]
    public void a_preview_job_asks_for_anchors_and_a_publish_job_refuses_them()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        string path = folder.File("score.ly", "{ c'4 }\n");
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.OpenDocument(path);
        LilyPortEngine engine = new LilyPortEngine();

        //Act
        PreviewJob preview = new PreviewJob(engine, document);
        PublishJob publish = new PublishJob(engine, document);

        //Assert
        preview.Option("point-and-click").Should().Be(true);
        publish.Option("point-and-click").Should().Be(false);

        //Anchors are the ONE option the engine's per-run seam carries, so
        //neither job has anything pending.
        preview.PendingOptions.Should().BeEmpty();
        publish.PendingOptions.Should().BeEmpty();
    }

    [Fact]
    public void a_layout_control_job_carries_its_options_and_reports_them_as_pending()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        string path = folder.File("score.ly", "{ c'4 }\n");
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.OpenDocument(path);
        LilyPortEngine engine = new LilyPortEngine();

        //Act
        LayoutControlJob job = new LayoutControlJob(
            engine, document, LayoutControl.PreviewOptions(new[] { "voices" }));

        //Assert
        job.Option("debug-voices").Should().Be(true);
        job.Option("point-and-click").Should().Be(true);
        job.IncludePath.Should().Contain(LayoutControl.AssetDirectory());

        //Everything but the anchors is carried, reported and not applied —
        //widening the engine's per-run seam is a change to the engine.
        job.PendingOptions.Should().Equal(
            "debug-voices", "include-settings");
    }

    [Fact]
    public void a_job_engraves_into_the_directory_the_source_is_read_from()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        string path = folder.File("score.ly", "{ c'4 }\n");
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.OpenDocument(path);

        //Act
        PreviewJob job = new PreviewJob(new LilyPortEngine(), document);

        //Assert
        job.FileName.Should().Be(path);
        job.Directory.Should().Be(folder.Path);
        job.Priority.Should().Be(2);
        job.Title.Should().Contain("score");
    }
}

/// <summary>When an automatic engrave is worth starting.</summary>
public class AutoCompileTests
{
    [Fact]
    public void an_unmodified_document_with_output_on_disk_is_not_worth_engraving()
    {
        //Arrange
        using TempFolder folder = new TempFolder();
        string path = folder.File("score.ly", "\\version \"" + LilyPortInfo.CompatibleWithVersion + "\"\n{ c'4 }\n");
        folder.File("score.svg", "<svg/>");
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.OpenDocument(path);

        //Act
        AutoCompileState state = AutoCompileState.For(document);

        //Assert
        state.MayCompile().Should().BeFalse();
    }

    [Fact]
    public void an_incomplete_document_is_never_engraved()
    {
        //Arrange
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.CreateDocument();
        AutoCompileState state = AutoCompileState.For(document);

        //Act: a half-typed expression
        document.Document.Text = "{ c'4 ";

        //Assert
        state.MayCompile().Should().BeFalse();
    }

    [Fact]
    public void a_complete_document_with_music_is_worth_engraving_once()
    {
        //Arrange
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.CreateDocument();
        AutoCompileState state = AutoCompileState.For(document);

        //Act
        document.Document.Text = "\\version \"" + LilyPortInfo.CompatibleWithVersion + "\"\n{ c'4 }\n";

        //Assert
        state.MayCompile().Should().BeTrue();

        //And not again until something changes: the tokens have not moved.
        state.MayCompile().Should().BeFalse();
    }

    [Fact]
    public void reformatting_does_not_make_a_document_worth_engraving_again()
    {
        //Arrange
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.CreateDocument();
        AutoCompileState state = AutoCompileState.For(document);
        document.Document.Text = "\\version \"" + LilyPortInfo.CompatibleWithVersion + "\"\n{ c'4 }\n";
        state.MayCompile().Should().BeTrue();

        //Act: only whitespace and a comment change
        document.Document.Text = "\\version \"" + LilyPortInfo.CompatibleWithVersion + "\"\n\n{  c'4  }  % a note\n";

        //Assert
        state.MayCompile().Should().BeFalse();
    }

    [Fact]
    public void an_empty_document_is_complete_but_never_worth_engraving()
    {
        //Arrange
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.CreateDocument();
        AutoCompileState state = AutoCompileState.For(document);

        //Act
        document.Document.Text = "   \n";

        //Assert
        state.MayCompile().Should().BeFalse();
    }
}

/// <summary>The estimated progress of a run.</summary>
public class EngraveProgressTests
{
    [Fact]
    public void a_run_with_no_history_gets_the_arbitrary_first_estimate()
    {
        //Arrange
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.CreateDocument();
        EngraveProgress progress = new EngraveProgress();
        FakeJob job = new FakeJob();

        //Act
        progress.Start(document, job, lineCount: 200, metaInfo: null);

        //Assert
        progress.IsRunning.Should().BeTrue();
        progress.Fraction.Should().BeLessThan(1.0);
    }

    [Fact]
    public void the_bar_never_quite_arrives_while_the_run_continues()
    {
        //Arrange
        DocumentManager documents = new DocumentManager();
        EditorDocument document = documents.CreateDocument();
        EngraveProgress progress = new EngraveProgress();
        FakeJob job = new FakeJob();
        progress.Start(document, job, lineCount: 0, metaInfo: null);

        //Act: pretend the estimate was wildly optimistic
        Thread.Sleep(10);

        //Assert
        progress.Fraction.Should().BeLessThanOrEqualTo(0.99);
    }
}
