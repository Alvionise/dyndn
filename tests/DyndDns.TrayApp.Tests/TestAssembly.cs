using Xunit;

// The dialogs and the monitoring window of this app are WinForms controls, and building, measuring and laying
// them out touches process-wide caches that are not thread-safe: two test classes doing that at the same time
// made the button bar of a dialog come out one pixel tall, which the layout guard reported as a failure. The
// suite is small, so it runs one test at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
