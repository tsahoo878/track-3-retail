# Review checklist

Use this for every change before it is merged. Tick it, or write why an item does not apply.

## Does it do what was asked
- [ ] Every acceptance criterion in the spec has a test, and the test name says which one.
- [ ] Invalid requests get a clear 4xx answer with a useful message, not a 500.
- [ ] Money is handled as decimal, rounded in one place, and never goes above what the customer paid.

## Data
- [ ] Database changes are a new numbered migration; an old migration is never edited.
- [ ] The migration matches the EF model (types, lengths, null rules, indexes).
- [ ] Dates and times are handled in one consistent way.
- [ ] Two people changing the same row at the same time cannot both win silently.

## Security and privacy
- [ ] No password, key, token or connection string with a real password is in the repository.
- [ ] No endpoint returns more fields than its caller needs (password hashes, full customer details).
- [ ] Personal data is not kept longer, or shared more widely, than it needs to be.
- [ ] Anything that touches the database for Claude or for analysts is read-only and limited to the views it needs.

## Performance
- [ ] A list endpoint does not run a query per row. Check the number of queries for a page of 25.
- [ ] Lists are paged.

## Tests
- [ ] The tests pass on a clean checkout, at any time of day and on any date.
- [ ] Each test asserts the thing its name says it asserts.
- [ ] Tests are reliable and repeatable.

## Code
- [ ] Controllers only call services; services hold the rules; repositories only talk to the database.
- [ ] No method does several unrelated jobs; long methods are split.
- [ ] README and CLAUDE.md still describe how the project really works.

## Delivery
- [ ] CI is green on the pull request.
- [ ] compose.yaml starts everything the README says it starts.
