import gleam/list
import gleeunit/should
import lustre_todos/shared/route

pub fn from_string_matches_each_route_test() {
  route.from_string("/") |> should.equal(route.AllTodos)
  route.from_string("/active") |> should.equal(route.ActiveTodos)
  route.from_string("/completed") |> should.equal(route.CompletedTodos)
}

pub fn from_string_ignores_query_fragment_and_case_test() {
  route.from_string("/Active?sort=asc#top") |> should.equal(route.ActiveTodos)
}

pub fn from_string_defaults_to_not_found_test() {
  route.from_string("/no/such/page") |> should.equal(route.NotFound)
}

pub fn to_string_round_trips_test() {
  [route.AllTodos, route.ActiveTodos, route.CompletedTodos]
  |> list.all(fn(r) { route.from_string(route.to_string(r)) == r })
  |> should.be_true
}
