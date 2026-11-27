// Picking up the ideas and the activities on the nights in the Program Builder, and putting them down on a night or back among the ideas.
// It is done with the pointer rather than the browser's own drag and drop, which shows no clear target, is awkward to pick up and does not
// work on a touch screen. Nothing here knows the program: it finds what is picked up by data-drag-kind ("idea" or "night") and data-drag-key,
// where it can be put down by data-drop-night (a date) and data-drop-ideas, and tells the page which was dropped on which.
window.ProgramDrag = (function () {
    const THRESHOLD = 5;      // pixels the pointer moves before something is picked up, so a click is still a click
    const EDGE = 70;          // pixels from the top or bottom of the window where it starts to scroll
    const NOT_FROM = "button, input, select, textarea, label, a, summary, details";

    let dotNet = null;
    let bound = false;
    let pending = null;       // pressed on something that can be picked up, not moved far enough yet
    let drag = null;          // what is being carried

    function init(reference) {
        dotNet = reference;
        if (bound) return;
        bound = true;
        document.addEventListener("pointerdown", onDown);
        document.addEventListener("pointermove", onMove);
        document.addEventListener("pointerup", onUp);
        document.addEventListener("pointercancel", cancel);
        document.addEventListener("keydown", function (e) { if (e.key === "Escape" && drag) cancel(); });
        // the browser's own drag of selected text or a link must not take over from this one
        document.addEventListener("dragstart", function (e) { if (pending || drag) e.preventDefault(); });
        // leaving the window or the page's tab while carrying drops nothing, rather than leaving it carried
        window.addEventListener("blur", cancel);
        document.addEventListener("contextmenu", function () { if (drag) cancel(); });
    }

    function onDown(e) {
        // a drag still going at a new press means its end was never seen: it is let go of before the new one starts
        if (drag) cancel();
        pending = null;
        if (e.pointerType === "mouse" && e.button !== 0) return;
        const target = e.target instanceof Element ? e.target : null;
        const source = target ? target.closest("[data-drag-kind]") : null;
        if (!source || target.closest(NOT_FROM)) return;
        // a finger only picks up by the handle, so the rest of the card still scrolls the page
        if (e.pointerType === "touch" && !target.closest(".drag-handle")) return;
        pending = { source: source, id: e.pointerId, x: e.clientX, y: e.clientY };
    }

    function onMove(e) {
        if (drag) {
            if (e.pointerId !== drag.id) return;
            // the button was let go where nothing heard it: that is the drop
            if (e.pointerType === "mouse" && e.buttons === 0) {
                drag.x = e.clientX;
                drag.y = e.clientY;
                drop(e.clientX, e.clientY);
                return;
            }
            drag.x = e.clientX;
            drag.y = e.clientY;
            e.preventDefault();
            place();
            return;
        }
        if (!pending || e.pointerId !== pending.id) return;
        if (Math.hypot(e.clientX - pending.x, e.clientY - pending.y) < THRESHOLD) return;
        start(e);
    }

    function start(e) {
        const source = pending.source;
        pending = null;
        const kind = source.getAttribute("data-drag-kind");
        const key = source.getAttribute("data-drag-key");
        const holder = source.closest(".night-card, .idea-card") || source;

        // what is carried is a copy of the card, held by the place it was picked up
        const box = holder.getBoundingClientRect();
        const ghost = holder.cloneNode(true);
        [ghost].concat(Array.from(ghost.querySelectorAll("*"))).forEach(function (n) {
            n.removeAttribute("id");
            n.removeAttribute("data-drop-night");
            n.removeAttribute("data-drop-ideas");
            n.removeAttribute("data-drag-kind");
            n.removeAttribute("data-drag-key");
            n.removeAttribute("name");
        });
        // what was typed in a box is not in the copy of it until it is put there
        const from = holder.querySelectorAll("input, select, textarea");
        ghost.querySelectorAll("input, select, textarea").forEach(function (n, i) { if (from[i]) n.value = from[i].value; });
        ghost.classList.add("pb-ghost");
        ghost.setAttribute("aria-hidden", "true");
        ghost.style.width = box.width + "px";
        ghost.style.height = box.height + "px";
        ghost.style.transformOrigin = (e.clientX - box.left) + "px " + (e.clientY - box.top) + "px";
        document.body.appendChild(ghost);

        try { source.setPointerCapture(e.pointerId); } catch (error) { /* it is followed without it */ }
        drag = { id: e.pointerId, source: source, kind: kind, key: key, holder: holder, ghost: ghost, x: e.clientX, y: e.clientY, offX: e.clientX - box.left, offY: e.clientY - box.top, over: null, frame: 0 };
        holder.classList.add("pb-dragged");
        document.body.classList.add("pb-dragging");
        // every place it can go is marked, so it is plain where it can be put down
        document.querySelectorAll("[data-drop-night]").forEach(function (n) {
            if (!(kind === "night" && n.getAttribute("data-drop-night") === key)) n.classList.add("pb-drop-ready");
        });
        if (kind === "night") document.querySelectorAll("[data-drop-ideas]").forEach(function (n) { n.classList.add("pb-drop-ready"); });
        // clicks that follow the drop (on a label, say) are not meant
        window.addEventListener("click", swallow, true);
        place();
        drag.frame = requestAnimationFrame(scrollLoop);
    }

    function swallow(e) {
        e.stopPropagation();
        e.preventDefault();
        window.removeEventListener("click", swallow, true);
    }

    function targetAt(x, y) {
        const element = document.elementFromPoint(x, y);
        const spot = element ? element.closest("[data-drop-night], [data-drop-ideas]") : null;
        if (!spot || !spot.classList.contains("pb-drop-ready")) return null;
        return spot;
    }

    function hintFor(spot) {
        if (spot.hasAttribute("data-drop-ideas")) return "Put back in the ideas";
        const there = (spot.querySelector(".night-activity") || {}).value;
        if (!there) return "Put it here";
        return drag.kind === "night" ? "Swap with " + there : "Replace " + there + " (it goes back to the ideas)";
    }

    function place() {
        drag.ghost.style.transform = "translate(" + (drag.x - drag.offX) + "px," + (drag.y - drag.offY) + "px) rotate(1.5deg)";
        const spot = targetAt(drag.x, drag.y);
        if (spot === drag.over) return;
        if (drag.over) {
            drag.over.classList.remove("pb-drop-over");
            drag.over.removeAttribute("data-hint");
        }
        drag.over = spot;
        if (spot) {
            spot.classList.add("pb-drop-over");
            spot.setAttribute("data-hint", hintFor(spot));
        }
    }

    // the window scrolls while the pointer is held near its top or bottom edge
    function scrollLoop() {
        if (!drag) return;
        let by = 0;
        if (drag.y < EDGE) by = -Math.ceil((EDGE - drag.y) / 4);
        else if (drag.y > window.innerHeight - EDGE) by = Math.ceil((drag.y - (window.innerHeight - EDGE)) / 4);
        if (by !== 0) {
            window.scrollBy(0, by);
            place();
        }
        drag.frame = requestAnimationFrame(scrollLoop);
    }

    function finish() {
        if (!drag) return null;
        cancelAnimationFrame(drag.frame);
        try { drag.source.releasePointerCapture(drag.id); } catch (error) { /* already let go */ }
        drag.ghost.remove();
        drag.holder.classList.remove("pb-dragged");
        if (drag.over) {
            drag.over.classList.remove("pb-drop-over");
            drag.over.removeAttribute("data-hint");
        }
        document.body.classList.remove("pb-dragging");
        document.querySelectorAll(".pb-drop-ready").forEach(function (n) { n.classList.remove("pb-drop-ready"); });
        const done = drag;
        drag = null;
        // if no click follows (the pointer was let go somewhere else), the swallowing of one is ended after a moment
        setTimeout(function () { window.removeEventListener("click", swallow, true); }, 0);
        return done;
    }

    function onUp(e) {
        if (pending && e.pointerId === pending.id) pending = null;
        if (!drag || e.pointerId !== drag.id) return;
        drop(e.clientX, e.clientY);
    }

    function drop(x, y) {
        const spot = targetAt(x, y);
        const done = finish();
        if (!spot || !dotNet) return;
        const onto = spot.hasAttribute("data-drop-ideas") ? "ideas" : "night";
        const to = onto === "night" ? spot.getAttribute("data-drop-night") : "";
        dotNet.invokeMethodAsync("DropDragged", done.kind, done.key, onto, to);
    }

    function cancel() {
        pending = null;
        finish();
    }

    return { init: init };
})();
