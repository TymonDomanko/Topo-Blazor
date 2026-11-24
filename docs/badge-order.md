# Badge order

The Badge Order page (Tools, `/badgeorder`) turns what the group has awarded since its last order into a cart at the national Scout Shop,
https://scoutshop.com.au.

## What it counts

The same figures as the Section/Group Report, for every unit of the group, from a day to today: milestones, Outdoor Adventure Skill stages, Special
Interest Areas, additional awards (Camper, Walkabout, years of service, First Aid and the rest), and the introduction badge for each Scout invested in
the period. It reads Terrain's records, and the years of service recorded in the group's store (docs/badge-store.md).

Each line is matched to a product in the shop by `Model/BadgeOrder/BadgeOrderBuilder.cs`. A badge with no match (Peak Awards, which the report counts
without saying which part, an additional award the shop does not sell, a fourth Cub year of service) is shown as "Not in the shop, order by hand" and is
left out of the cart link.

## The day it counts from

The day of the last order is a store record against the group's own id (`order_last`, its "awarded" day), written by "Mark order placed" after a
confirm step, so every leader's copy of Topo counts from the same day. When none has been recorded the leader picks a day. "Mark order placed" needs the
group's store event (Settings, Years of service, "Create the records event").

## The cart link

`https://scoutshop.com.au/cart/<variant id>:<quantity>,<variant id>:<quantity>` adds each badge to a cart and goes to the shop's checkout, with nothing
paid. An order of more than 40 lines is split into several links. Topo never signs in to the shop or pays: the leader signs in to the Scout Shop with
their own account, and some badges can only be bought by an adult leader.

## Keeping the shop list up to date

`Model/BadgeOrder/ScoutShopCatalog.cs` holds the shop's variant ids and RRP prices as they were on 25 November 2026. If the shop re-creates a product its
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
