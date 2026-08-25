// xUnit runs test CLASSES in parallel by default. Every class here boots a real host
// through WebApplicationFactory, and two of those starting at the same moment race over
// the shared entry-point hook used to intercept Program.cs — the visible symptom was a
// test class occasionally coming up with none of its logging configuration applied.
//
// These are integration tests: each one is already a full request through the pipeline,
// so the parallelism buys little and costs determinism.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
