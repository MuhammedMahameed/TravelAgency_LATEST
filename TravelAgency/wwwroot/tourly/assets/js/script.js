"use strict";

// elements
const header = document.querySelector("[data-header]");
const nav = document.querySelector("[data-navbar]");
const overlay = document.querySelector("[data-overlay]");
const navOpenBtn = document.querySelector("[data-nav-open-btn]");
const navCloseBtn = document.querySelector("[data-nav-close-btn]");
const navLinks = document.querySelectorAll("[data-nav-link]");
const goTopBtn = document.querySelector("[data-go-top]");

// open / close nav
const openNav = () => {
    nav?.classList.add("active");
    overlay?.classList.add("active");
};
const closeNav = () => {
    nav?.classList.remove("active");
    overlay?.classList.remove("active");
};

navOpenBtn?.addEventListener("click", openNav);
navCloseBtn?.addEventListener("click", closeNav);
overlay?.addEventListener("click", closeNav);

navLinks?.forEach(link => link.addEventListener("click", closeNav));

// header active + go top
window.addEventListener("scroll", () => {
    const scrolled = window.scrollY >= 200;

    if (scrolled) {
        header?.classList.add("active");
        goTopBtn?.classList.add("active");
    } else {
        header?.classList.remove("active");
        goTopBtn?.classList.remove("active");
    }
});
