# Badge order

The Badge Order page (Tools, `/badgeorder`) turns what the group has awarded since its last order into a cart at the national Scout Shop,
https://scoutshop.com.au.

## What it counts

The same figures as the Section/Group Report, for the unit chosen (or every unit of the group, the default), from a day to today: milestones, Outdoor Adventure Skill stages, Special
Interest Areas, additional awards (Camper, Walkabout, years of service, First Aid and the rest), and the introduction badge for each Scout invested in
the period. It reads Terrain's records, and the years of service recorded in the group's store (docs/badge-store.md).

Each line is matched to a product in the shop by `Model/BadgeOrder/BadgeOrderBuilder.cs`. A badge with no match (Peak Awards, which the report counts
without saying which part, an additional award the shop does not sell, a fourth Cub year of service) is shown as "Not in the shop, order by hand" and is
left out of the cart link.

## The day it counts from

Each unit has its own last order day, a store record against the unit's id (`order_last`, its "awarded" day), written by "Mark order placed" after a
confirm step, so every leader's copy of Topo counts from the same days. Because it is kept for each unit:

- an order for one section moves only that section's day, so the other sections' badges are still counted for the next order;
- an order for the whole group counts each section from its own last order, so badges already ordered for a section are not ordered again;
- "Mark order placed" records the day for the units the order was for.

A unit with no order recorded counts from a day the leader picks. "Change date" replaces every chosen unit's day for that order. The records need
the group's store event (Settings, Years of service, "Create the records event"). Topo cannot see the shop, so an order that is placed but not marked
as placed will be counted again by the next person.

## The cart link

`https://scoutshop.com.au/cart/<variant id>:<quantity>,<variant id>:<quantity>` adds each badge to a cart and goes to the shop's checkout, with nothing
paid. An order is one link unless it would be longer than about 2,000 characters (some 100 lines), when it is split. Topo never signs in to the shop or pays: the leader signs in to the Scout Shop with
their own account, and some badges can only be bought by an adult leader.

## Keeping the shop list up to date

`Model/BadgeOrder/ScoutShopCatalog.cs` holds the shop's variant ids (and the prices, kept for reference but not shown on the page) as they were on 25 November 2026. If the shop re-creates a product its
numbers change and that badge shows as "not in the shop" until the list is refreshed. To refresh it, open any scoutshop.com.au product page, run this in
the browser console, and paste the result over the `Data` text in the file:

```js
const handles = [/* the product handles listed in the file's first column */];
const router = window.__reactRouterDataRouter, lines = [];
for (const h of handles) {
  await router.navigate("/products/" + h);
  let p = null;
  for (let i = 0; i < 40 && !p; i++) {
    const d = router.state.loaderData["routes/($locale).products.$productHandle"];
    if (d && d.product && d.product.handle === h) p = d.product; else await new Promise(r => setTimeout(r, 150));
  }
  for (const o of p.options) {
    lines.push([h, p.title, o.name, o.optionValues.map(v => { const f = v.firstSelectableVariant; return v.name + "=" + f.id.replace("gid://shopify/ProductVariant/", "") + "@" + f.price.amount; }).join("; ")].join(" | "));
  }
}
console.log(lines.join("\n"));
```
