# Spike-then-Port: Prototyping in Isolation, Then AI-Assisted Porting

## The idea
Build the tricky part of a feature as a small, fast CLI prototype in a separate repo, validate the logic there, then use an AI coding agent to port that logic into the real (web) application.

## Why it works
- Iterating on core logic is much faster in a minimal CLI than inside a big app.
- A working prototype becomes an *executable specification* — far better for an AI agent to reproduce than a prose description.
- Separates "does the logic work?" from "does it fit the target architecture?" — two different problems solved one at a time.

## This is a recognized pattern
- **Spike** (Extreme Programming, Kent Beck / Ward Cunningham): a time-boxed, throwaway piece of code used to explore a technical question before committing to production work. Normally discarded — here it's reused as a reference, making this a **spike-then-port** variant.
- **Tracer bullet** (*The Pragmatic Programmer*, Hunt & Thomas): a thin, working, end-to-end slice meant to evolve into production code rather than be thrown away — closer in spirit to reusing the logic.
- Further reading: Martin Fowler's bliki ("Spike"), *Extreme Programming Explained* (Beck).

## Recommendations for doing it well

1. **Separate core logic from CLI plumbing.** Keep business logic in modules that don't know about stdin/stdout/args. The CLI is just a thin wrapper. This makes the "logic to port" clean and interface-agnostic.

2. **Write unit tests for the core logic (not the CLI plumbing).**
    - Tests pin down edge cases and behavior that prose descriptions miss.
    - Port the tests along with the logic — treat the port as "done" when it passes the same tests.
    - This holds even if the target app currently has no tests; their value is as a spec/verification harness for the port, independent of what the target repo does afterward.
    - Skip testing stdout formatting, arg parsing, exit codes — that's CLI-specific and won't transfer.

3. **Brief the agent on what must change, not just what to copy.** CLI → web isn't 1:1: add async/request lifecycle, statelessness, untrusted-input validation, HTTP error handling, concurrency. Say this explicitly so the agent doesn't naively copy blocking calls, global state, or console output into a web handler.

4. **Language choice:**
    - If the target app's language is already fixed (e.g. an existing F# codebase), match it — consistency beats picking whatever language the agent is strongest in.
    - AI agents generally produce more idiomatic code in more mainstream languages (e.g. C# vs F#) due to more training data. If porting into a less mainstream language, review more critically for idiomatic style, not just correctness.
    - Feed the agent 1–2 real examples of the *target* codebase's existing idioms/handlers as a style reference — this does more for idiomatic output than instructions alone.
    - If both prototype and target share a runtime (e.g. both .NET), consider extracting the core logic into a shared library instead of reimplementing it at all.

## Summary workflow
1. Prototype core logic as a CLI app, in isolation, fast to iterate.
2. Extract the logic into clean, interface-agnostic modules.
3. Write unit tests against that core logic.
4. Hand the agent: the core logic + the tests + explicit notes on target-environment differences + example idioms from the target repo.
5. Port logic and tests together; treat the port as correct once tests pass.