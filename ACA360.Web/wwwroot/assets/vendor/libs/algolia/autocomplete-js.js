! function (e, t) {
    if ("object" == typeof exports && "object" == typeof module) module.exports = t();
    else if ("function" == typeof define && define.amd) define([], t);
    else {
        var n = t();
        for (var r in n) ("object" == typeof exports ? exports : e)[r] = n[r]
    }
}(self, (function () {
    return function () {
        "use strict";
        var e = {
            d: function (t, n) {
                for (var r in n) e.o(n, r) && !e.o(t, r) && Object.defineProperty(t, r, {
                    enumerable: !0,
                    get: n[r]
                })
            },
            o: function (e, t) {
                return Object.prototype.hasOwnProperty.call(e, t)
            },
            r: function (e) {
                "undefined" != typeof Symbol && Symbol.toStringTag && Object.defineProperty(e, Symbol.toStringTag, {
                    value: "Module"
                }), Object.defineProperty(e, "__esModule", {
                    value: !0
                })
            }
        },
            t = {};

        function n(e, t) {
            var n = void 0;
            return function () {
                for (var r = arguments.length, o = new Array(r), i = 0; i < r; i++) o[i] = arguments[i];
                n && clearTimeout(n), n = setTimeout((function () {
                    return e.apply(void 0, o)
                }), t)
            }
        }

        function r(e) {
            return e !== Object(e)
        }

        function o(e, t) {
            if (e === t) return !0;
            if (r(e) || r(t) || "function" == typeof e || "function" == typeof t) return e === t;
            if (Object.keys(e).length !== Object.keys(t).length) return !1;
            for (var n = 0, i = Object.keys(e); n < i.length; n++) {
                var u = i[n];
                if (!(u in t)) return !1;
                if (!o(e[u], t[u])) return !1
            }
            return !0
        }
        e.r(t), e.d(t, {
            autocomplete: function () {
                return Gr
            }
        });
        var i = function () { };

        function u(e) {
            var t = e.item,
                n = e.items,
                r = void 0 === n ? [] : n;
            return {
                index: t.__autocomplete_indexName,
                items: [t],
                positions: [1 + r.findIndex((function (e) {
                    return e.objectID === t.objectID
                }))],
                queryID: t.__autocomplete_queryID,
                algoliaSource: ["autocomplete"]
            }
        }

        function a(e, t) {
            return function (e) {
                if (Array.isArray(e)) return e
            }(e) || function (e, t) {
                var n = null == e ? null : "undefined" != typeof Symbol && e[Symbol.iterator] || e["@@iterator"];
                if (null != n) {
                    var r, o, i, u, a = [],
                        c = !0,
                        l = !1;
                    try {
                        if (i = (n = n.call(e)).next, 0 === t) {
                            if (Object(n) !== n) return;
                            c = !1
                        } else
                            for (; !(c = (r = i.call(n)).done) && (a.push(r.value), a.length !== t); c = !0);
                    } catch (e) {
                        l = !0, o = e
                    } finally {
                        try {
                            if (!c && null != n.return && (u = n.return(), Object(u) !== u)) return
                        } finally {
                            if (l) throw o
                        }
                    }
                    return a
                }
            }(e, t) || function (e, t) {
                if (!e) return;
                if ("string" == typeof e) return c(e, t);
                var n = Object.prototype.toString.call(e).slice(8, -1);
                "Object" === n && e.constructor && (n = e.constructor.name);
                if ("Map" === n || "Set" === n) return Array.from(e);
                if ("Arguments" === n || /^(?:Ui|I)nt(?:8|16|32)(?:Clamped)?Array$/.test(n)) return c(e, t)
            }(e, t) || function () {
                throw new TypeError("Invalid attempt to destructure non-iterable instance.\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.")
            }()
        }

        function c(e, t) {
            (null == t || t > e.length) && (t = e.length);
            for (var n = 0, r = new Array(t); n < t; n++) r[n] = e[n];
            return r
        }
        var l = ["items"],
            s = ["items"];

        function f(e) {
            return f = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (e) {
                return typeof e
            } : function (e) {
                return e && "function" == typeof Symbol && e.constructor === Symbol && e !== Symbol.prototype ? "symbol" : typeof e
            }, f(e)
        }

        function p(e) {
            return function (e) {
                if (Array.isArray(e)) return m(e)
            }(e) || function (e) {
                if ("undefined" != typeof Symbol && null != e[Symbol.iterator] || null != e["@@iterator"]) return Array.from(e)
            }(e) || function (e, t) {
                if (!e) return;
                if ("string" == typeof e) return m(e, t);
                var n = Object.prototype.toString.call(e).slice(8, -1);
                "Object" === n && e.constructor && (n = e.constructor.name);
                if ("Map" === n || "Set" === n) return Array.from(e);
                if ("Arguments" === n || /^(?:Ui|I)nt(?:8|16|32)(?:Clamped)?Array$/.test(n)) return m(e, t)
            }(e) || function () {
                throw new TypeError("Invalid attempt to spread non-iterable instance.\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.")
            }()
        }

        function m(e, t) {
            (null == t || t > e.length) && (t = e.length);
            for (var n = 0, r = new Array(t); n < t; n++) r[n] = e[n];
            return r
        }

        function v(e, t) {
            if (null == e) return {};
            var n, r, o = function (e, t) {
                if (null == e) return {};
                var n, r, o = {},
                    i = Object.keys(e);
                for (r = 0; r < i.length; r++) n = i[r], t.indexOf(n) >= 0 || (o[n] = e[n]);
                return o
            }(e, t);
            if (Object.getOwnPropertySymbols) {
                var i = Object.getOwnPropertySymbols(e);
                for (r = 0; r < i.length; r++) n = i[r], t.indexOf(n) >= 0 || Object.prototype.propertyIsEnumerable.call(e, n) && (o[n] = e[n])
            }
            return o
        }

        function y(e, t) {
            var n = Object.keys(e);
            if (Object.getOwnPropertySymbols) {
                var r = Object.getOwnPropertySymbols(e);
                t && (r = r.filter((function (t) {
                    return Object.getOwnPropertyDescriptor(e, t).enumerable
                }))), n.push.apply(n, r)
            }
            return n
        }

        function d(e) {
            for (var t = 1; t < arguments.length; t++) {
                var n = null != arguments[t] ? arguments[t] : {};
                t % 2 ? y(Object(n), !0).forEach((function (t) {
                    b(e, t, n[t])
                })) : Object.getOwnPropertyDescriptors ? Object.defineProperties(e, Object.getOwnPropertyDescriptors(n)) : y(Object(n)).forEach((function (t) {
                    Object.defineProperty(e, t, Object.getOwnPropertyDescriptor(n, t))
                }))
            }
            return e
        }

        function b(e, t, n) {
            return (t = function (e) {
                var t = function (e, t) {
                    if ("object" !== f(e) || null === e) return e;
                    var n = e[Symbol.toPrimitive];
                    if (void 0 !== n) {
                        var r = n.call(e, t || "default");
                        if ("object" !== f(r)) return r;
                        throw new TypeError("@@toPrimitive must return a primitive value.")
                    }
                    return ("string" === t ? String : Number)(e)
                }(e, "string");
                return "symbol" === f(t) ? t : String(t)
            }(t)) in e ? Object.defineProperty(e, t, {
                value: n,
                enumerable: !0,
                configurable: !0,
                writable: !0
            }) : e[t] = n, e
        }

        function g(e) {
            return e.map((function (e) {
                var t = e.items,
                    n = v(e, l);
                return d(d({}, n), {}, {
                    objectIDs: (null == t ? void 0 : t.map((function (e) {
                        return e.objectID
                    }))) || n.objectIDs
                })
            }))
        }

        function h(e) {
            var t, n, r, o = (t = a((e.version || "").split(".").map(Number), 2), n = t[0], r = t[1], n >= 3 || 2 === n && r >= 4 || 1 === n && r >= 10);

            function i(t, n, r) {
                if (o && void 0 !== r) {
                    var i = r[0].__autocomplete_algoliaCredentials,
                        u = {
                            "X-Algolia-Application-Id": i.appId,
                            "X-Algolia-API-Key": i.apiKey
                        };
                    e.apply(void 0, [t].concat(p(n), [{
                        headers: u
                    }]))
                } else e.apply(void 0, [t].concat(p(n)))
            }
            return {
                init: function (t, n) {
                    e("init", {
                        appId: t,
                        apiKey: n
                    })
                },
                setAuthenticatedUserToken: function (t) {
                    e("setAuthenticatedUserToken", t)
                },
                setUserToken: function (t) {
                    e("setUserToken", t)
                },
                clickedObjectIDsAfterSearch: function () {
                    for (var e = arguments.length, t = new Array(e), n = 0; n < e; n++) t[n] = arguments[n];
                    t.length > 0 && i("clickedObjectIDsAfterSearch", g(t), t[0].items)
                },
                clickedObjectIDs: function () {
                    for (var e = arguments.length, t = new Array(e), n = 0; n < e; n++) t[n] = arguments[n];
                    t.length > 0 && i("clickedObjectIDs", g(t), t[0].items)
                },
                clickedFilters: function () {
                    for (var t = arguments.length, n = new Array(t), r = 0; r < t; r++) n[r] = arguments[r];
                    n.length > 0 && e.apply(void 0, ["clickedFilters"].concat(n))
                },
                convertedObjectIDsAfterSearch: function () {
                    for (var e = arguments.length, t = new Array(e), n = 0; n < e; n++) t[n] = arguments[n];
                    t.length > 0 && i("convertedObjectIDsAfterSearch", g(t), t[0].items)
                },
                convertedObjectIDs: function () {
                    for (var e = arguments.length, t = new Array(e), n = 0; n < e; n++) t[n] = arguments[n];
                    t.length > 0 && i("convertedObjectIDs", g(t), t[0].items)
                },
                convertedFilters: function () {
                    for (var t = arguments.length, n = new Array(t), r = 0; r < t; r++) n[r] = arguments[r];
                    n.length > 0 && e.apply(void 0, ["convertedFilters"].concat(n))
                },
                viewedObjectIDs: function () {
                    for (var e = arguments.length, t = new Array(e), n = 0; n < e; n++) t[n] = arguments[n];
                    t.length > 0 && t.reduce((function (e, t) {
                        var n = t.items,
                            r = v(t, s);
                        return [].concat(p(e), p(function (e) {
                            for (var t = arguments.length > 1 && void 0 !== arguments[1] ? arguments[1] : 20, n = [], r = 0; r < e.objectIDs.length; r += t) n.push(d(d({}, e), {}, {
                                objectIDs: e.objectIDs.slice(r, r + t)
                            }));
                            return n
                        }(d(d({}, r), {}, {
                            objectIDs: (null == n ? void 0 : n.map((function (e) {
                                return e.objectID
                            }))) || r.objectIDs
                        })).map((function (e) {
                            return {
                                items: n,
                                payload: e
                            }
                        }))))
                    }), []).forEach((function (e) {
                        var t = e.items;
                        return i("viewedObjectIDs", [e.payload], t)
                    }))
                },
                viewedFilters: function () {
                    for (var t = arguments.length, n = new Array(t), r = 0; r < t; r++) n[r] = arguments[r];
                    n.length > 0 && e.apply(void 0, ["viewedFilters"].concat(n))
                }
            }
        }

        function O(e) {
            var t = e.items.reduce((function (e, t) {
                var n;
                return e[t.__autocomplete_indexName] = (null !== (n = e[t.__autocomplete_indexName]) && void 0 !== n ? n : []).concat(t), e
            }), {});
            return Object.keys(t).map((function (e) {
                return {
                    index: e,
                    items: t[e],
                    algoliaSource: ["autocomplete"]
                }
            }))
        }

        function _(e) {
            return e.objectID && e.__autocomplete_indexName && e.__autocomplete_queryID
        }

        function S(e) {
            return S = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (e) {
                return typeof e
            } : function (e) {
                return e && "function" == typeof Symbol && e.constructor === Symbol && e !== Symbol.prototype ? "symbol" : typeof e
            }, S(e)
        }

        function j(e) {
            return function (e) {
                if (Array.isArray(e)) return P(e)
            }(e) || function (e) {
                if ("undefined" != typeof Symbol && null != e[Symbol.iterator] || null != e["@@iterator"]) return Array.from(e)
            }(e) || function (e, t) {
                if (!e) return;
                if ("string" == typeof e) return P(e, t);
                var n = Object.prototype.toString.call(e).slice(8, -1);
                "Object" === n && e.constructor && (n = e.constructor.name);
                if ("Map" === n || "Set" === n) return Array.from(e);
                if ("Arguments" === n || /^(?:Ui|I)nt(?:8|16|32)(?:Clamped)?Array$/.test(n)) return P(e, t)
            }(e) || function () {
                throw new TypeError("Invalid attempt to spread non-iterable instance.\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.")
            }()
        }

        function P(e, t) {
            (null == t || t > e.length) && (t = e.length);
            for (var n = 0, r = new Array(t); n < t; n++) r[n] = e[n];
            return r
        }

        function w(e, t) {
            var n = Object.keys(e);
            if (Object.getOwnPropertySymbols) {
                var r = Object.getOwnPropertySymbols(e);
                t && (r = r.filter((function (t) {
                    return Object.getOwnPropertyDescriptor(e, t).enumerable
                }))), n.push.apply(n, r)
            }
            return n
        }

        function I(e) {
            for (var t = 1; t < arguments.length; t++) {
                var n = null != arguments[t] ? arguments[t] : {};
                t % 2 ? w(Object(n), !0).forEach((function (t) {
                    E(e, t, n[t])
                })) : Object.getOwnPropertyDescriptors ? Object.defineProperties(e, Object.getOwnPropertyDescriptors(n)) : w(Object(n)).forEach((function (t) {
                    Object.defineProperty(e, t, Object.getOwnPropertyDescriptor(n, t))
                }))
            }
            return e
        }

        function E(e, t, n) {
            return (t = function (e) {
                var t = function (e, t) {
                    if ("object" !== S(e) || null === e) return e;
                    var n = e[Symbol.toPrimitive];
                    if (void 0 !== n) {
                        var r = n.call(e, t || "default");
                        if ("object" !== S(r)) return r;
                        throw new TypeError("@@toPrimitive must return a primitive value.")
                    }
                    return ("string" === t ? String : Number)(e)
                }(e, "string");
                return "symbol" === S(t) ? t : String(t)
            }(t)) in e ? Object.defineProperty(e, t, {
                value: n,
                enumerable: !0,
                configurable: !0,
                writable: !0
            }) : e[t] = n, e
        }
        var A = "2.15.0",
            D = "https://cdn.jsdelivr.net/npm/search-insights@".concat(A, "/dist/search-insights.min.js"),
            C = n((function (e) {
                var t = e.onItemsChange,
                    n = e.items,
                    r = e.insights,
                    o = e.state;
                t({
                    insights: r,
                    insightsEvents: O({
                        items: n
                    }).map((function (e) {
                        return I({
                            eventName: "Items Viewed"
                        }, e)
                    })),
                    state: o
                })
            }), 400);

        function k(e) {
            var t = function (e) {
                return I({
                    onItemsChange: function (e) {
                        var t = e.insights,
                            n = e.insightsEvents,
                            r = e.state;
                        t.viewedObjectIDs.apply(t, j(n.map((function (e) {
                            return I(I({}, e), {}, {
                                algoliaSource: x(e.algoliaSource, r.context)
                            })
                        }))))
                    },
                    onSelect: function (e) {
                        var t = e.insights,
                            n = e.insightsEvents,
                            r = e.state;
                        t.clickedObjectIDsAfterSearch.apply(t, j(n.map((function (e) {
                            return I(I({}, e), {}, {
                                algoliaSource: x(e.algoliaSource, r.context)
                            })
                        }))))
                    },
                    onActive: i,
                    __autocomplete_clickAnalytics: !0
                }, e)
            }(e),
                r = t.insightsClient,
                a = t.insightsInitParams,
                c = t.onItemsChange,
                l = t.onSelect,
                s = t.onActive,
                f = t.__autocomplete_clickAnalytics,
                p = r;
            if (r || function (e) {
                if ("undefined" != typeof window) e({
                    window: window
                })
            }((function (e) {
                var t = e.window,
                    n = t.AlgoliaAnalyticsObject || "aa";
                "string" == typeof n && (p = t[n]), p || (t.AlgoliaAnalyticsObject = n, t[n] || (t[n] = function () {
                    t[n].queue || (t[n].queue = []);
                    for (var e = arguments.length, r = new Array(e), o = 0; o < e; o++) r[o] = arguments[o];
                    t[n].queue.push(r)
                }), t[n].version = A, p = t[n], function (e) {
                    try {
                        var t = e.document.createElement("script");
                        t.async = !0, t.src = D, t.onerror = function () { }, document.body.appendChild(t)
                    } catch (e) { }
                }(t))
            })), !p) return {};
            a && p("init", I({
                partial: !0
            }, a));
            var m = h(p),
                v = {
                    current: []
                },
                y = n((function (e) {
                    var t = e.state;
                    if (t.isOpen) {
                        var n = t.collections.reduce((function (e, t) {
                            return [].concat(j(e), j(t.items))
                        }), []).filter(_);
                        o(v.current.map((function (e) {
                            return e.objectID
                        })), n.map((function (e) {
                            return e.objectID
                        }))) || (v.current = n, n.length > 0 && C({
                            onItemsChange: c,
                            items: n,
                            insights: m,
                            state: t
                        }))
                    }
                }), 0);
            return {
                name: "aa.algoliaInsightsPlugin",
                subscribe: function (e) {
                    var t = e.setContext,
                        n = e.onSelect,
                        r = e.onActive;

                    function o(e) {
                        t({
                            algoliaInsightsPlugin: {
                                __algoliaSearchParameters: I(I({}, f ? {
                                    clickAnalytics: !0
                                } : {}), e ? {
                                    userToken: T(e)
                                } : {}),
                                insights: m
                            }
                        })
                    }
                    p("addAlgoliaAgent", "insights-plugin"), o(), p("onUserTokenChange", (function (e) {
                        o(e)
                    })), p("getUserToken", null, (function (e, t) {
                        o(t)
                    })), n((function (e) {
                        var t = e.item,
                            n = e.state,
                            r = e.event,
                            o = e.source;
                        _(t) && l({
                            state: n,
                            event: r,
                            insights: m,
                            item: t,
                            insightsEvents: [I({
                                eventName: "Item Selected"
                            }, u({
                                item: t,
                                items: o.getItems().filter(_)
                            }))]
                        })
                    })), r((function (e) {
                        var t = e.item,
                            n = e.source,
                            r = e.state,
                            o = e.event;
                        _(t) && s({
                            state: r,
                            event: o,
                            insights: m,
                            item: t,
                            insightsEvents: [I({
                                eventName: "Item Active"
                            }, u({
                                item: t,
                                items: n.getItems().filter(_)
                            }))]
                        })
                    }))
                },
                onStateChange: function (e) {
                    var t = e.state;
                    y({
                        state: t
                    })
                },
                __autocomplete_pluginOptions: e
            }
        }

        function x() {
            var e, t = arguments.length > 1 ? arguments[1] : void 0;
            return [].concat(j(arguments.length > 0 && void 0 !== arguments[0] ? arguments[0] : []), ["autocomplete-internal"], j(null !== (e = t.algoliaInsightsPlugin) && void 0 !== e && e.__automaticInsights ? ["autocomplete-automatic"] : []))
        }

        function T(e) {
            return "number" == typeof e ? e.toString() : e
        }
        var N, B = !0;

        function q(e) {
            return q = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (e) {
                return typeof e
            } : function (e) {
                return e && "function" == typeof Symbol && e.constructor === Symbol && e !== Symbol.prototype ? "symbol" : typeof e
            }, q(e)
        }

        function R(e, t) {
            var n = Object.keys(e);
            if (Object.getOwnPropertySymbols) {
                var r = Object.getOwnPropertySymbols(e);
                t && (r = r.filter((function (t) {
                    return Object.getOwnPropertyDescriptor(e, t).enumerable
                }))), n.push.apply(n, r)
            }
            return n
        }

        function L(e, t, n) {
            return (t = function (e) {
                var t = function (e, t) {
                    if ("object" !== q(e) || null === e) return e;
                    var n = e[Symbol.toPrimitive];
                    if (void 0 !== n) {
                        var r = n.call(e, t || "default");
                        if ("object" !== q(r)) return r;
                        throw new TypeError("@@toPrimitive must return a primitive value.")
                    }
                    return ("string" === t ? String : Number)(e)
                }(e, "string");
                return "symbol" === q(t) ? t : String(t)
            }(t)) in e ? Object.defineProperty(e, t, {
                value: n,
                enumerable: !0,
                configurable: !0,
                writable: !0
            }) : e[t] = n, e
        }

        function F(e, t, n) {
            var r, o = t.initialState;
            return {
                getState: function () {
                    return o
                },
                dispatch: function (r, i) {
                    var u = function (e) {
                        for (var t = 1; t < arguments.length; t++) {
                            var n = null != arguments[t] ? arguments[t] : {};
                            t % 2 ? R(Object(n), !0).forEach((function (t) {
                                L(e, t, n[t])
                            })) : Object.getOwnPropertyDescriptors ? Object.defineProperties(e, Object.getOwnPropertyDescriptors(n)) : R(Object(n)).forEach((function (t) {
                                Object.defineProperty(e, t, Object.getOwnPropertyDescriptor(n, t))
                            }))
                        }
                        return e
                    }({}, o);
                    o = e(o, {
                        type: r,
                        props: t,
                        payload: i
                    }), n({
                        state: o,
                        prevState: u
                    })
                },
                pendingRequests: (r = [], {
                    add: function (e) {
                        return r.push(e), e.finally((function () {
                            r = r.filter((function (t) {
                                return t !== e
                            }))
                        }))
                    },
                    cancelAll: function () {
                        r.forEach((function (e) {
                            return e.cancel()
                        }))
                    },
                    isEmpty: function () {
                        return 0 === r.length
                    },
                    wait: function (e) {
                        return B ? (B = !1, N = e ? Promise.race([Promise.all(r), new Promise((function (t) {
                            return setTimeout(t, e)
                        }))]) : Promise.all(r), N.then((function () {
                            B = !0
                        }))) : N
                    }
                })
            }
        }

        function U(e) {
            return e.reduce((function (e, t) {
                return e.concat(t)
            }), [])
        }

        function M(e) {
            return M = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (e) {
                return typeof e
            } : function (e) {
                return e && "function" == typeof Symbol && e.constructor === Symbol && e !== Symbol.prototype ? "symbol" : typeof e
            }, M(e)
        }

        function H(e, t) {
            var n = Object.keys(e);
            if (Object.getOwnPropertySymbols) {
                var r = Object.getOwnPropertySymbols(e);
                t && (r = r.filter((function (t) {
                    return Object.getOwnPropertyDescriptor(e, t).enumerable
                }))), n.push.apply(n, r)
            }
            return n
        }

        function V(e) {
            for (var t = 1; t < arguments.length; t++) {
                var n = null != arguments[t] ? arguments[t] : {};
                t % 2 ? H(Object(n), !0).forEach((function (t) {
                    W(e, t, n[t])
                })) : Object.getOwnPropertyDescriptors ? Object.defineProperties(e, Object.getOwnPropertyDescriptors(n)) : H(Object(n)).forEach((function (t) {
                    Object.defineProperty(e, t, Object.getOwnPropertyDescriptor(n, t))
                }))
            }
            return e
        }

        function W(e, t, n) {
            return (t = function (e) {
                var t = function (e, t) {
                    if ("object" !== M(e) || null === e) return e;
                    var n = e[Symbol.toPrimitive];
                    if (void 0 !== n) {
                        var r = n.call(e, t || "default");
                        if ("object" !== M(r)) return r;
                        throw new TypeError("@@toPrimitive must return a primitive value.")
                    }
                    return ("string" === t ? String : Number)(e)
                }(e, "string");
                return "symbol" === M(t) ? t : String(t)
            }(t)) in e ? Object.defineProperty(e, t, {
                value: n,
                enumerable: !0,
                configurable: !0,
                writable: !0
            }) : e[t] = n, e
        }

        function $(e) {
            return 0 === e.collections.length ? 0 : e.collections.reduce((function (e, t) {
                return e + t.items.length
            }), 0)
        }
        var Q = 0;

        function K() {
            return "autocomplete-".concat(Q++)
        }
        var z = function () { };

        function G(e, t) {
            var n = Object.keys(e);
            if (Object.getOwnPropertySymbols) {
                var r = Object.getOwnPropertySymbols(e);
                t && (r = r.filter((function (t) {
                    return Object.getOwnPropertyDescriptor(e, t).enumerable
                }))), n.push.apply(n, r)
            }
            return n
        }

        function J(e) {
            for (var t = 1; t < arguments.length; t++) {
                var n = null != arguments[t] ? arguments[t] : {};
                t % 2 ? G(Object(n), !0).forEach((function (t) {
                    X(e, t, n[t])
                })) : Object.getOwnPropertyDescriptors ? Object.defineProperties(e, Object.getOwnPropertyDescriptors(n)) : G(Object(n)).forEach((function (t) {
                    Object.defineProperty(e, t, Object.getOwnPropertyDescriptor(n, t))
                }))
            }
            return e
        }

        function X(e, t, n) {
            return (t = function (e) {
                var t = function (e, t) {
                    if ("object" !== Y(e) || null === e) return e;
                    var n = e[Symbol.toPrimitive];
                    if (void 0 !== n) {
                        var r = n.call(e, t || "default");
                        if ("object" !== Y(r)) return r;
                        throw new TypeError("@@toPrimitive must return a primitive value.")
                    }
                    return ("string" === t ? String : Number)(e)
                }(e, "string");
                return "symbol" === Y(t) ? t : String(t)
            }(t)) in e ? Object.defineProperty(e, t, {
                value: n,
                enumerable: !0,
                configurable: !0,
                writable: !0
            }) : e[t] = n, e
        }

        function Y(e) {
            return Y = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (e) {
                return typeof e
            } : function (e) {
                return e && "function" == typeof Symbol && e.constructor === Symbol && e !== Symbol.prototype ? "symbol" : typeof e
            }, Y(e)
        }

        function Z(e) {
            return Z = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (e) {
                return typeof e
            } : function (e) {
                return e && "function" == typeof Symbol && e.constructor === Symbol && e !== Symbol.prototype ? "symbol" : typeof e
            }, Z(e)
        }

        function ee(e) {
            return function (e) {
                if (Array.isArray(e)) return te(e)
            }(e) || function (e) {
                if ("undefined" != typeof Symbol && null != e[Symbol.iterator] || null != e["@@iterator"]) return Array.from(e)
            }(e) || function (e, t) {
                if (!e) return;
                if ("string" == typeof e) return te(e, t);
                var n = Object.prototype.toString.call(e).slice(8, -1);
                "Object" === n && e.constructor && (n = e.constructor.name);
                if ("Map" === n || "Set" === n) return Array.from(e);
                if ("Arguments" === n || /^(?:Ui|I)nt(?:8|16|32)(?:Clamped)?Array$/.test(n)) return te(e, t)
            }(e) || function () {
                throw new TypeError("Invalid attempt to spread non-iterable instance.\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.")
            }()
        }

        function te(e, t) {
            (null == t || t > e.length) && (t = e.length);
            for (var n = 0, r = new Array(t); n < t; n++) r[n] = e[n];
            return r
        }

        function ne(e, t) {
            var n = Object.keys(e);
            if (Object.getOwnPropertySymbols) {
                var r = Object.getOwnPropertySymbols(e);
                t && (r = r.filter((function (t) {
                    return Object.getOwnPropertyDescriptor(e, t).enumerable
                }))), n.push.apply(n, r)
            }
            return n
        }

        function re(e) {
            for (var t = 1; t < arguments.length; t++) {
                var n = null != arguments[t] ? arguments[t] : {};
                t % 2 ? ne(Object(n), !0).forEach((function (t) {
                    oe(e, t, n[t])
                })) : Object.getOwnPropertyDescriptors ? Object.defineProperties(e, Object.getOwnPropertyDescriptors(n)) : ne(Object(n)).forEach((function (t) {
                    Object.defineProperty(e, t, Object.getOwnPropertyDescriptor(n, t))
                }))
            }
            return e
        }

        function oe(e, t, n) {
            return (t = function (e) {
                var t = function (e, t) {
                    if ("object" !== Z(e) || null === e) return e;
                    var n = e[Symbol.toPrimitive];
                    if (void 0 !== n) {
                        var r = n.call(e, t || "default");
                        if ("object" !== Z(r)) return r;
                        throw new TypeError("@@toPrimitive must return a primitive value.")
                    }
                    return ("string" === t ? String : Number)(e)
                }(e, "string");
                return "symbol" === Z(t) ? t : String(t)
            }(t)) in e ? Object.defineProperty(e, t, {
                value: n,
                enumerable: !0,
                configurable: !0,
                writable: !0
            }) : e[t] = n, e
        }

        function ie(e, t) {
            var n, r = "undefined" != typeof window ? window : {},
                o = e.plugins || [];
            return re(re({
                debug: !1,
                openOnFocus: !1,
                enterKeyHint: void 0,
                ignoreCompositionEvents: !1,
                placeholder: "",
                autoFocus: !1,
                defaultActiveItemId: null,
                stallThreshold: 300,
                insights: void 0,
                environment: r,
                shouldPanelOpen: function (e) {
                    return $(e.state) > 0
                },
                reshape: function (e) {
                    return e.sources
                }
            }, e), {}, {
                id: null !== (n = e.id) && void 0 !== n ? n : K(),
                plugins: o,
                initialState: re({
                    activeItemId: null,
                    query: "",
                    completion: null,
                    collections: [],
                    isOpen: !1,
                    status: "idle",
                    context: {}
                }, e.initialState),
                onStateChange: function (t) {
                    var n;
                    null === (n = e.onStateChange) || void 0 === n || n.call(e, t), o.forEach((function (e) {
                        var n;
                        return null === (n = e.onStateChange) || void 0 === n ? void 0 : n.call(e, t)
                    }))
                },
                onSubmit: function (t) {
                    var n;
                    null === (n = e.onSubmit) || void 0 === n || n.call(e, t), o.forEach((function (e) {
                        var n;
                        return null === (n = e.onSubmit) || void 0 === n ? void 0 : n.call(e, t)
                    }))
                },
                onReset: function (t) {
                    var n;
                    null === (n = e.onReset) || void 0 === n || n.call(e, t), o.forEach((function (e) {
                        var n;
                        return null === (n = e.onReset) || void 0 === n ? void 0 : n.call(e, t)
                    }))
                },
                getSources: function (n) {
                    return Promise.all([].concat(ee(o.map((function (e) {
                        return e.getSources
                    }))), [e.getSources]).filter(Boolean).map((function (e) {
                        return function (e, t) {
                            var n = [];
                            return Promise.resolve(e(t)).then((function (e) {
                                return Array.isArray(e), Promise.all(e.filter((function (e) {
                                    return Boolean(e)
                                })).map((function (e) {
                                    if (e.sourceId, n.includes(e.sourceId)) throw new Error("[Autocomplete] The `sourceId` ".concat(JSON.stringify(e.sourceId), " is not unique."));
                                    n.push(e.sourceId);
                                    var t = {
                                        getItemInputValue: function (e) {
                                            return e.state.query
                                        },
                                        getItemUrl: function () { },
                                        onSelect: function (e) {
                                            (0, e.setIsOpen)(!1)
                                        },
                                        onActive: z,
                                        onResolve: z
                                    };
                                    Object.keys(t).forEach((function (e) {
                                        t[e].__default = !0
                                    }));
                                    var r = J(J({}, t), e);
                                    return Promise.resolve(r)
                                })))
                            }))
                        }(e, n)
                    }))).then((function (e) {
                        return U(e)
                    })).then((function (e) {
                        return e.map((function (e) {
                            return re(re({}, e), {}, {
                                onSelect: function (n) {
                                    e.onSelect(n), t.forEach((function (e) {
                                        var t;
                                        return null === (t = e.onSelect) || void 0 === t ? void 0 : t.call(e, n)
                                    }))
                                },
                                onActive: function (n) {
                                    e.onActive(n), t.forEach((function (e) {
                                        var t;
                                        return null === (t = e.onActive) || void 0 === t ? void 0 : t.call(e, n)
                                    }))
                                },
                                onResolve: function (n) {
                                    e.onResolve(n), t.forEach((function (e) {
                                        var t;
                                        return null === (t = e.onResolve) || void 0 === t ? void 0 : t.call(e, n)
                                    }))
                                }
                            })
                        }))
                    }))
                },
                navigator: re({
                    navigate: function (e) {
                        var t = e.itemUrl;
                        r.location.assign(t)
                    },
                    navigateNewTab: function (e) {
                        var t = e.itemUrl,
                            n = r.open(t, "_blank", "noopener");
                        null == n || n.focus()
                    },
                    navigateNewWindow: function (e) {
                        var t = e.itemUrl;
                        r.open(t, "_blank", "noopener")
                    }
                }, e.navigator)
            })
        }

        function ue(e) {
            return ue = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (e) {
                return typeof e
            } : function (e) {
                return e && "function" == typeof Symbol && e.constructor === Symbol && e !== Symbol.prototype ? "symbol" : typeof e
            }, ue(e)
        }

        function ae(e, t) {
            var n = Object.keys(e);
            if (Object.getOwnPropertySymbols) {
                var r = Object.getOwnPropertySymbols(e);
                t && (r = r.filter((function (t) {
                    return Object.getOwnPropertyDescriptor(e, t).enumerable
                }))), n.push.apply(n, r)
            }
            return n
        }

        function ce(e) {
            for (var t = 1; t < arguments.length; t++) {
                var n = null != arguments[t] ? arguments[t] : {};
                t % 2 ? ae(Object(n), !0).forEach((function (t) {
                    le(e, t, n[t])
                })) : Object.getOwnPropertyDescriptors ? Object.defineProperties(e, Object.getOwnPropertyDescriptors(n)) : ae(Object(n)).forEach((function (t) {
                    Object.defineProperty(e, t, Object.getOwnPropertyDescriptor(n, t))
                }))
            }
            return e
        }

        function le(e, t, n) {
            return (t = function (e) {
                var t = function (e, t) {
                    if ("object" !== ue(e) || null === e) return e;
                    var n = e[Symbol.toPrimitive];
                    if (void 0 !== n) {
                        var r = n.call(e, t || "default");
                        if ("object" !== ue(r)) return r;
                        throw new TypeError("@@toPrimitive must return a primitive value.")
                    }
                    return ("string" === t ? String : Number)(e)
                }(e, "string");
                return "symbol" === ue(t) ? t : String(t)
            }(t)) in e ? Object.defineProperty(e, t, {
                value: n,
                enumerable: !0,
                configurable: !0,
                writable: !0
            }) : e[t] = n, e
        }

        function se(e) {
            return se = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (e) {
                return typeof e
            } : function (e) {
                return e && "function" == typeof Symbol && e.constructor === Symbol && e !== Symbol.prototype ? "symbol" : typeof e
            }, se(e)
        }

        function fe(e, t) {
            var n = Object.keys(e);
            if (Object.getOwnPropertySymbols) {
                var r = Object.getOwnPropertySymbols(e);
                t && (r = r.filter((function (t) {
                    return Object.getOwnPropertyDescriptor(e, t).enumerable
                }))), n.push.apply(n, r)
            }
            return n
        }

        function pe(e) {
            for (var t = 1; t < arguments.length; t++) {
                var n = null != arguments[t] ? arguments[t] : {};
                t % 2 ? fe(Object(n), !0).forEach((function (t) {
                    me(e, t, n[t])
                })) : Object.getOwnPropertyDescriptors ? Object.defineProperties(e, Object.getOwnPropertyDescriptors(n)) : fe(Object(n)).forEach((function (t) {
                    Object.defineProperty(e, t, Object.getOwnPropertyDescriptor(n, t))
                }))
            }
            return e
        }

        function me(e, t, n) {
            return (t = function (e) {
                var t = function (e, t) {
                    if ("object" !== se(e) || null === e) return e;
                    var n = e[Symbol.toPrimitive];
                    if (void 0 !== n) {
                        var r = n.call(e, t || "default");
                        if ("object" !== se(r)) return r;
                        throw new TypeError("@@toPrimitive must return a primitive value.")
                    }
                    return ("string" === t ? String : Number)(e)
                }(e, "string");
                return "symbol" === se(t) ? t : String(t)
            }(t)) in e ? Object.defineProperty(e, t, {
                value: n,
                enumerable: !0,
                configurable: !0,
                writable: !0
            }) : e[t] = n, e
        }

        function ve(e) {
            return function (e) {
                if (Array.isArray(e)) return ye(e)
            }(e) || function (e) {
                if ("undefined" != typeof Symbol && null != e[Symbol.iterator] || null != e["@@iterator"]) return Array.from(e)
            }(e) || function (e, t) {
                if (!e) return;
                if ("string" == typeof e) return ye(e, t);
                var n = Object.prototype.toString.call(e).slice(8, -1);
                "Object" === n && e.constructor && (n = e.constructor.name);
                if ("Map" === n || "Set" === n) return Array.from(e);
                if ("Arguments" === n || /^(?:Ui|I)nt(?:8|16|32)(?:Clamped)?Array$/.test(n)) return ye(e, t)
            }(e) || function () {
                throw new TypeError("Invalid attempt to spread non-iterable instance.\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.")
            }()
        }

        function ye(e, t) {
            (null == t || t > e.length) && (t = e.length);
            for (var n = 0, r = new Array(t); n < t; n++) r[n] = e[n];
            return r
        }

        function de(e) {
            return Boolean(e.execute)
        }

        function be(e, t, n) {
            if (o = e, Boolean(null == o ? void 0 : o.execute)) {
                var r = "algolia" === e.requesterId ? Object.assign.apply(Object, [{}].concat(ve(Object.keys(n.context).map((function (e) {
                    var t;
                    return null === (t = n.context[e]) || void 0 === t ? void 0 : t.__algoliaSearchParameters
                }))))) : {};
                return pe(pe({}, e), {}, {
                    requests: e.queries.map((function (n) {
                        return {
                            query: "algolia" === e.requesterId ? pe(pe({}, n), {}, {
                                params: pe(pe({}, r), n.params)
                            }) : n,
                            sourceId: t,
                            transformResponse: e.transformResponse
                        }
                    }))
                })
            }
            var o;
            return {
                items: e,
                sourceId: t
            }
        }

        function ge(e) {
            var t = e.reduce((function (e, t) {
                if (!de(t)) return e.push(t), e;
                var n = t.searchClient,
                    r = t.execute,
                    o = t.requesterId,
                    i = t.requests,
                    u = e.find((function (e) {
                        return de(t) && de(e) && e.searchClient === n && Boolean(o) && e.requesterId === o
                    }));
                if (u) {
                    var a;
                    (a = u.items).push.apply(a, ve(i))
                } else {
                    var c = {
                        execute: r,
                        requesterId: o,
                        items: i,
                        searchClient: n
                    };
                    e.push(c)
                }
                return e
            }), []).map((function (e) {
                if (!de(e)) return Promise.resolve(e);
                var t = e,
                    n = t.execute,
                    r = t.items;
                return n({
                    searchClient: t.searchClient,
                    requests: r
                })
            }));
            return Promise.all(t).then((function (e) {
                return U(e)
            }))
        }

        function he(e, t, n) {
            return t.map((function (t) {
                var r, o = e.filter((function (e) {
                    return e.sourceId === t.sourceId
                })),
                    i = o.map((function (e) {
                        return e.items
                    })),
                    u = o[0].transformResponse,
                    a = u ? u({
                        results: r = i,
                        hits: r.map((function (e) {
                            return e.hits
                        })).filter(Boolean),
                        facetHits: r.map((function (e) {
                            var t;
                            return null === (t = e.facetHits) || void 0 === t ? void 0 : t.map((function (e) {
                                return {
                                    label: e.value,
                                    count: e.count,
                                    _highlightResult: {
                                        label: {
                                            value: e.highlighted
                                        }
                                    }
                                }
                            }))
                        })).filter(Boolean)
                    }) : i;
                return t.onResolve({
                    source: t,
                    results: i,
                    items: a,
                    state: n.getState()
                }), Array.isArray(a), a.every(Boolean), 'The `getItems` function from source "'.concat(t.sourceId, '" must return an array of items but returned ').concat(JSON.stringify(void 0), ".\n\nDid you forget to return items?\n\nSee: https://www.algolia.com/doc/ui-libraries/autocomplete/core-concepts/sources/#param-getitems"), {
                    source: t,
                    items: a
                }
            }))
        }

        function Oe(e, t) {
            var n = t;
            return {
                then: function (t, r) {
                    return Oe(e.then(je(t, n, e), je(r, n, e)), n)
                },
                catch: function (t) {
                    return Oe(e.catch(je(t, n, e)), n)
                },
                finally: function (t) {
                    return t && n.onCancelList.push(t), Oe(e.finally(je(t && function () {
                        return n.onCancelList = [], t()
                    }, n, e)), n)
                },
                cancel: function () {
                    n.isCanceled = !0;
                    var e = n.onCancelList;
                    n.onCancelList = [], e.forEach((function (e) {
                        e()
                    }))
                },
                isCanceled: function () {
                    return !0 === n.isCanceled
                }
            }
        }

        function _e(e) {
            return Oe(new Promise((function (t, n) {
                return e(t, n)
            })), {
                isCanceled: !1,
                onCancelList: []
            })
        }

        function Se(e) {
            return Oe(e, {
                isCanceled: !1,
                onCancelList: []
            })
        }

        function je(e, t, n) {
            return e ? function (n) {
                return t.isCanceled ? n : e(n)
            } : n
        }

        function Pe(e) {
            var t = function (e) {
                var t = e.collections.map((function (e) {
                    return e.items.length
                })).reduce((function (e, t, n) {
                    var r = (e[n - 1] || 0) + t;
                    return e.push(r), e
                }), []).reduce((function (t, n) {
                    return n <= e.activeItemId ? t + 1 : t
                }), 0);
                return e.collections[t]
            }(e);
            if (!t) return null;
            var n = t.items[function (e) {
                for (var t = e.state, n = e.collection, r = !1, o = 0, i = 0; !1 === r;) {
                    var u = t.collections[o];
                    if (u === n) {
                        r = !0;
                        break
                    }
                    i += u.items.length, o++
                }
                return t.activeItemId - i
            }({
                state: e,
                collection: t
            })],
                r = t.source;
            return {
                item: n,
                itemInputValue: r.getItemInputValue({
                    item: n,
                    state: e
                }),
                itemUrl: r.getItemUrl({
                    item: n,
                    state: e
                }),
                source: r
            }
        }

        function we(e) {
            return we = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (e) {
                return typeof e
            } : function (e) {
                return e && "function" == typeof Symbol && e.constructor === Symbol && e !== Symbol.prototype ? "symbol" : typeof e
            }, we(e)
        }
        _e.resolve = function (e) {
            return Se(Promise.resolve(e))
        }, _e.reject = function (e) {
            return Se(Promise.reject(e))
        };
        var Ie = ["event", "nextState", "props", "query", "refresh", "store"];

        function Ee(e, t) {
            var n = Object.keys(e);
            if (Object.getOwnPropertySymbols) {
                var r = Object.getOwnPropertySymbols(e);
                t && (r = r.filter((function (t) {
                    return Object.getOwnPropertyDescriptor(e, t).enumerable
                }))), n.push.apply(n, r)
            }
            return n
        }

        function Ae(e) {
            for (var t = 1; t < arguments.length; t++) {
                var n = null != arguments[t] ? arguments[t] : {};
                t % 2 ? Ee(Object(n), !0).forEach((function (t) {
                    De(e, t, n[t])
                })) : Object.getOwnPropertyDescriptors ? Object.defineProperties(e, Object.getOwnPropertyDescriptors(n)) : Ee(Object(n)).forEach((function (t) {
                    Object.defineProperty(e, t, Object.getOwnPropertyDescriptor(n, t))
                }))
            }
            return e
        }

        function De(e, t, n) {
            return (t = function (e) {
                var t = function (e, t) {
                    if ("object" !== we(e) || null === e) return e;
                    var n = e[Symbol.toPrimitive];
                    if (void 0 !== n) {
                        var r = n.call(e, t || "default");
                        if ("object" !== we(r)) return r;
                        throw new TypeError("@@toPrimitive must return a primitive value.")
                    }
                    return ("string" === t ? String : Number)(e)
                }(e, "string");
                return "symbol" === we(t) ? t : String(t)
            }(t)) in e ? Object.defineProperty(e, t, {
                value: n,
                enumerable: !0,
                configurable: !0,
                writable: !0
            }) : e[t] = n, e
        }

        function Ce(e, t) {
            if (null == e) return {};
            var n, r, o = function (e, t) {
                if (null == e) return {};
                var n, r, o = {},
                    i = Object.keys(e);
                for (r = 0; r < i.length; r++) n = i[r], t.indexOf(n) >= 0 || (o[n] = e[n]);
                return o
            }(e, t);
            if (Object.getOwnPropertySymbols) {
                var i = Object.getOwnPropertySymbols(e);
                for (r = 0; r < i.length; r++) n = i[r], t.indexOf(n) >= 0 || Object.prototype.propertyIsEnumerable.call(e, n) && (o[n] = e[n])
            }
            return o
        }
        var ke, xe, Te, Ne = null,
            Be = (ke = -1, xe = -1, Te = void 0, function (e) {
                var t = ++ke;
                return Promise.resolve(e).then((function (e) {
                    return Te && t < xe ? Te : (xe = t, Te = e, e)
                }))
            });

        function qe(e) {
            var t = e.event,
                n = e.nextState,
                r = void 0 === n ? {} : n,
                o = e.props,
                i = e.query,
                u = e.refresh,
                a = e.store,
                c = Ce(e, Ie);
            Ne && o.environment.clearTimeout(Ne);
            var l = c.setCollections,
                s = c.setIsOpen,
                f = c.setQuery,
                p = c.setActiveItemId,
                m = c.setStatus,
                v = c.setContext;
            if (f(i), p(o.defaultActiveItemId), !i && !1 === o.openOnFocus) {
                var y, d = a.getState().collections.map((function (e) {
                    return Ae(Ae({}, e), {}, {
                        items: []
                    })
                }));
                m("idle"), l(d), s(null !== (y = r.isOpen) && void 0 !== y ? y : o.shouldPanelOpen({
                    state: a.getState()
                }));
                var b = Se(Be(d).then((function () {
                    return Promise.resolve()
                })));
                return a.pendingRequests.add(b)
            }
            m("loading"), Ne = o.environment.setTimeout((function () {
                m("stalled")
            }), o.stallThreshold);
            var g = Se(Be(o.getSources(Ae({
                query: i,
                refresh: u,
                state: a.getState()
            }, c)).then((function (e) {
                return Promise.all(e.map((function (e) {
                    return Promise.resolve(e.getItems(Ae({
                        query: i,
                        refresh: u,
                        state: a.getState()
                    }, c))).then((function (t) {
                        return be(t, e.sourceId, a.getState())
                    }))
                }))).then(ge).then((function (t) {
                    var n, r = t.some((function (e) {
                        return function (e) {
                            return !Array.isArray(e) && Boolean(null == e ? void 0 : e._automaticInsights)
                        }(e.items)
                    }));
                    r && v({
                        algoliaInsightsPlugin: Ae(Ae({}, (null === (n = a.getState().context) || void 0 === n ? void 0 : n.algoliaInsightsPlugin) || {}), {}, {
                            __automaticInsights: r
                        })
                    });
                    return he(t, e, a)
                })).then((function (e) {
                    return function (e) {
                        var t = e.collections,
                            n = e.props,
                            r = e.state,
                            o = t.reduce((function (e, t) {
                                return ce(ce({}, e), {}, le({}, t.source.sourceId, ce(ce({}, t.source), {}, {
                                    getItems: function () {
                                        return U(t.items)
                                    }
                                })))
                            }), {}),
                            i = n.plugins.reduce((function (e, t) {
                                return t.reshape ? t.reshape(e) : e
                            }), {
                                sourcesBySourceId: o,
                                state: r
                            }).sourcesBySourceId;
                        return U(n.reshape({
                            sourcesBySourceId: i,
                            sources: Object.values(i),
                            state: r
                        })).filter(Boolean).map((function (e) {
                            return {
                                source: e,
                                items: e.getItems()
                            }
                        }))
                    }({
                        collections: e,
                        props: o,
                        state: a.getState()
                    })
                }))
            })))).then((function (e) {
                var n;
                m("idle"), l(e);
                var f = o.shouldPanelOpen({
                    state: a.getState()
                });
                s(null !== (n = r.isOpen) && void 0 !== n ? n : o.openOnFocus && !i && f || f);
                var p = Pe(a.getState());
                if (null !== a.getState().activeItemId && p) {
                    var v = p.item,
                        y = p.itemInputValue,
                        d = p.itemUrl,
                        b = p.source;
                    b.onActive(Ae({
                        event: t,
                        item: v,
                        itemInputValue: y,
                        itemUrl: d,
                        refresh: u,
                        source: b,
                        state: a.getState()
                    }, c))
                }
            })).finally((function () {
                m("idle"), Ne && o.environment.clearTimeout(Ne)
            }));
            return a.pendingRequests.add(g)
        }

        function Re(e, t, n) {
            return [e, null == n ? void 0 : n.sourceId, t].filter(Boolean).join("-").replace(/\s/g, "")
        }

        function Le(e, t) {
            var n = "undefined" != typeof Symbol && e[Symbol.iterator] || e["@@iterator"];
            if (!n) {
                if (Array.isArray(e) || (n = function (e, t) {
                    if (!e) return;
                    if ("string" == typeof e) return Fe(e, t);
                    var n = Object.prototype.toString.call(e).slice(8, -1);
                    "Object" === n && e.constructor && (n = e.constructor.name);
                    if ("Map" === n || "Set" === n) return Array.from(e);
                    if ("Arguments" === n || /^(?:Ui|I)nt(?:8|16|32)(?:Clamped)?Array$/.test(n)) return Fe(e, t)
                }(e)) || t && e && "number" == typeof e.length) {
                    n && (e = n);
                    var r = 0,
                        o = function () { };
                    return {
                        s: o,
                        n: function () {
                            return r >= e.length ? {
                                done: !0
                            } : {
                                done: !1,
                                value: e[r++]
                            }
                        },
                        e: function (e) {
                            throw e
                        },
                        f: o
                    }
                }
                throw new TypeError("Invalid attempt to iterate non-iterable instance.\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.")
            }
            var i, u = !0,
                a = !1;
            return {
                s: function () {
                    n = n.call(e)
                },
                n: function () {
                    var e = n.next();
                    return u = e.done, e
                },
                e: function (e) {
                    a = !0, i = e
                },
                f: function () {
                    try {
                        u || null == n.return || n.return()
                    } finally {
                        if (a) throw i
                    }
                }
            }
        }

        function Fe(e, t) {
            (null == t || t > e.length) && (t = e.length);
            for (var n = 0, r = new Array(t); n < t; n++) r[n] = e[n];
            return r
        }
        var Ue = function (e, t) {
            var n, r = !1,
                o = [],
                i = Le(e);
            try {
                for (i.s(); !(n = i.n()).done;) {
                    var u, a, c, l = null === (u = n.value.__autocomplete_pluginOptions) || void 0 === u || null === (a = (c = u).awaitSubmit) || void 0 === a ? void 0 : a.call(c);
                    if ("number" == typeof l) o.push(l);
                    else if (!0 === l) {
                        r = !0;
                        break
                    }
                }
            } catch (e) {
                i.e(e)
            } finally {
                i.f()
            }
            return r ? t.wait() : o.length > 0 ? t.wait(Math.max.apply(Math, o)) : void 0
        };

        function Me(e) {
            return Me = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (e) {
                return typeof e
            } : function (e) {
                return e && "function" == typeof Symbol && e.constructor === Symbol && e !== Symbol.prototype ? "symbol" : typeof e
            }, Me(e)
        }
        var He = ["event", "props", "refresh", "store"];

        function Ve(e, t) {
            var n = Object.keys(e);
            if (Object.getOwnPropertySymbols) {
                var r = Object.getOwnPropertySymbols(e);
                t && (r = r.filter((function (t) {
                    return Object.getOwnPropertyDescriptor(e, t).enumerable
                }))), n.push.apply(n, r)
            }
            return n
        }

        function We(e) {
            for (var t = 1; t < arguments.length; t++) {
                var n = null != arguments[t] ? arguments[t] : {};
                t % 2 ? Ve(Object(n), !0).forEach((function (t) {
                    $e(e, t, n[t])
                })) : Object.getOwnPropertyDescriptors ? Object.defineProperties(e, Object.getOwnPropertyDescriptors(n)) : Ve(Object(n)).forEach((function (t) {
                    Object.defineProperty(e, t, Object.getOwnPropertyDescriptor(n, t))
                }))
            }
            return e
        }

        function $e(e, t, n) {
            return (t = function (e) {
                var t = function (e, t) {
                    if ("object" !== Me(e) || null === e) return e;
                    var n = e[Symbol.toPrimitive];
                    if (void 0 !== n) {
                        var r = n.call(e, t || "default");
                        if ("object" !== Me(r)) return r;
                        throw new TypeError("@@toPrimitive must return a primitive value.")
                    }
                    return ("string" === t ? String : Number)(e)
                }(e, "string");
                return "symbol" === Me(t) ? t : String(t)
            }(t)) in e ? Object.defineProperty(e, t, {
                value: n,
                enumerable: !0,
                configurable: !0,
                writable: !0
            }) : e[t] = n, e
        }

        function Qe(e, t) {
            if (null == e) return {};
            var n, r, o = function (e, t) {
                if (null == e) return {};
                var n, r, o = {},
                    i = Object.keys(e);
                for (r = 0; r < i.length; r++) n = i[r], t.indexOf(n) >= 0 || (o[n] = e[n]);
                return o
            }(e, t);
            if (Object.getOwnPropertySymbols) {
                var i = Object.getOwnPropertySymbols(e);
                for (r = 0; r < i.length; r++) n = i[r], t.indexOf(n) >= 0 || Object.prototype.propertyIsEnumerable.call(e, n) && (o[n] = e[n])
            }
            return o
        }
        var Ke = /((gt|sm)-|galaxy nexus)|samsung[- ]|samsungbrowser/i;

        function ze(e) {
            return e.nativeEvent || e
        }

        function Ge(e) {
            return Ge = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (e) {
                return typeof e
            } : function (e) {
                return e && "function" == typeof Symbol && e.constructor === Symbol && e !== Symbol.prototype ? "symbol" : typeof e
            }, Ge(e)
        }
        var Je = ["props", "refresh", "store"],
            Xe = ["inputElement", "formElement", "panelElement"],
            Ye = ["inputElement"],
            Ze = ["inputElement", "maxLength"],
            et = ["source"],
            tt = ["item", "source"];

        function nt(e, t) {
            var n = Object.keys(e);
            if (Object.getOwnPropertySymbols) {
                var r = Object.getOwnPropertySymbols(e);
                t && (r = r.filter((function (t) {
                    return Object.getOwnPropertyDescriptor(e, t).enumerable
                }))), n.push.apply(n, r)
            }
            return n
        }

        function rt(e) {
            for (var t = 1; t < arguments.length; t++) {
                var n = null != arguments[t] ? arguments[t] : {};
                t % 2 ? nt(Object(n), !0).forEach((function (t) {
                    ot(e, t, n[t])
                })) : Object.getOwnPropertyDescriptors ? Object.defineProperties(e, Object.getOwnPropertyDescriptors(n)) : nt(Object(n)).forEach((function (t) {
                    Object.defineProperty(e, t, Object.getOwnPropertyDescriptor(n, t))
                }))
            }
            return e
        }

        function ot(e, t, n) {
            return (t = function (e) {
                var t = function (e, t) {
                    if ("object" !== Ge(e) || null === e) return e;
                    var n = e[Symbol.toPrimitive];
                    if (void 0 !== n) {
                        var r = n.call(e, t || "default");
                        if ("object" !== Ge(r)) return r;
                        throw new TypeError("@@toPrimitive must return a primitive value.")
                    }
                    return ("string" === t ? String : Number)(e)
                }(e, "string");
                return "symbol" === Ge(t) ? t : String(t)
            }(t)) in e ? Object.defineProperty(e, t, {
                value: n,
                enumerable: !0,
                configurable: !0,
                writable: !0
            }) : e[t] = n, e
        }

        function it(e, t) {
            if (null == e) return {};
            var n, r, o = function (e, t) {
                if (null == e) return {};
                var n, r, o = {},
                    i = Object.keys(e);
                for (r = 0; r < i.length; r++) n = i[r], t.indexOf(n) >= 0 || (o[n] = e[n]);
                return o
            }(e, t);
            if (Object.getOwnPropertySymbols) {
                var i = Object.getOwnPropertySymbols(e);
                for (r = 0; r < i.length; r++) n = i[r], t.indexOf(n) >= 0 || Object.prototype.propertyIsEnumerable.call(e, n) && (o[n] = e[n])
            }
            return o
        }

        function ut(e) {
            var t = e.props,
                n = e.refresh,
                r = e.store,
                o = it(e, Je);
            return {
                getEnvironmentProps: function (e) {
                    var n = e.inputElement,
                        o = e.formElement,
                        i = e.panelElement;

                    function u(e) {
                        !r.getState().isOpen && r.pendingRequests.isEmpty() || e.target === n || !1 === [o, i].some((function (t) {
                            return n = t, r = e.target, n === r || n.contains(r);
                            var n, r
                        })) && (r.dispatch("blur", null), t.debug || r.pendingRequests.cancelAll())
                    }
                    return rt({
                        onTouchStart: u,
                        onMouseDown: u,
                        onTouchMove: function (e) {
                            !1 !== r.getState().isOpen && n === t.environment.document.activeElement && e.target !== n && n.blur()
                        }
                    }, it(e, Xe))
                },
                getRootProps: function (e) {
                    return rt({
                        role: "combobox",
                        "aria-expanded": r.getState().isOpen,
                        "aria-haspopup": "listbox",
                        "aria-controls": r.getState().isOpen ? r.getState().collections.map((function (e) {
                            var n = e.source;
                            return Re(t.id, "list", n)
                        })).join(" ") : void 0,
                        "aria-labelledby": Re(t.id, "label")
                    }, e)
                },
                getFormProps: function (e) {
                    e.inputElement;
                    var i = it(e, Ye),
                        u = function (i) {
                            var u;
                            t.onSubmit(rt({
                                event: i,
                                refresh: n,
                                state: r.getState()
                            }, o)), r.dispatch("submit", null), null === (u = e.inputElement) || void 0 === u || u.blur()
                        };
                    return rt({
                        action: "",
                        noValidate: !0,
                        role: "search",
                        onSubmit: function (e) {
                            e.preventDefault();
                            var n = Ue(t.plugins, r.pendingRequests);
                            void 0 !== n ? n.then((function () {
                                return u(e)
                            })) : u(e)
                        },
                        onReset: function (i) {
                            var u;
                            i.preventDefault(), t.onReset(rt({
                                event: i,
                                refresh: n,
                                state: r.getState()
                            }, o)), r.dispatch("reset", null), null === (u = e.inputElement) || void 0 === u || u.focus()
                        }
                    }, i)
                },
                getLabelProps: function (e) {
                    return rt({
                        htmlFor: Re(t.id, "input"),
                        id: Re(t.id, "label")
                    }, e)
                },
                getInputProps: function (e) {
                    var i;

                    function u(e) {
                        (t.openOnFocus || Boolean(r.getState().query)) && qe(rt({
                            event: e,
                            props: t,
                            query: r.getState().completion || r.getState().query,
                            refresh: n,
                            store: r
                        }, o)), r.dispatch("focus", null)
                    }
                    var a = e || {},
                        c = (a.inputElement, a.maxLength),
                        l = void 0 === c ? 512 : c,
                        s = it(a, Ze),
                        f = Pe(r.getState()),
                        p = function (e) {
                            return Boolean(e && e.match(Ke))
                        }((null === (i = t.environment.navigator) || void 0 === i ? void 0 : i.userAgent) || ""),
                        m = t.enterKeyHint || (null != f && f.itemUrl && !p ? "go" : "search");
                    return rt({
                        "aria-autocomplete": "both",
                        "aria-activedescendant": r.getState().isOpen && null !== r.getState().activeItemId ? Re(t.id, "item-".concat(r.getState().activeItemId), null == f ? void 0 : f.source) : void 0,
                        "aria-controls": r.getState().isOpen ? r.getState().collections.filter((function (e) {
                            return e.items.length > 0
                        })).map((function (e) {
                            var n = e.source;
                            return Re(t.id, "list", n)
                        })).join(" ") : void 0,
                        "aria-labelledby": Re(t.id, "label"),
                        value: r.getState().completion || r.getState().query,
                        id: Re(t.id, "input"),
                        autoComplete: "off",
                        autoCorrect: "off",
                        autoCapitalize: "off",
                        enterKeyHint: m,
                        spellCheck: "false",
                        autoFocus: t.autoFocus,
                        placeholder: t.placeholder,
                        maxLength: l,
                        type: "search",
                        onChange: function (e) {
                            var i = e.currentTarget.value;
                            t.ignoreCompositionEvents && ze(e).isComposing ? o.setQuery(i) : qe(rt({
                                event: e,
                                props: t,
                                query: i.slice(0, l),
                                refresh: n,
                                store: r
                            }, o))
                        },
                        onCompositionEnd: function (e) {
                            qe(rt({
                                event: e,
                                props: t,
                                query: e.currentTarget.value.slice(0, l),
                                refresh: n,
                                store: r
                            }, o))
                        },
                        onKeyDown: function (e) {
                            ze(e).isComposing || function (e) {
                                var t = e.event,
                                    n = e.props,
                                    r = e.refresh,
                                    o = e.store,
                                    i = Qe(e, He);
                                if ("ArrowUp" === t.key || "ArrowDown" === t.key) {
                                    var u = function () {
                                        var e = Pe(o.getState()),
                                            t = n.environment.document.getElementById(Re(n.id, "item-".concat(o.getState().activeItemId), null == e ? void 0 : e.source));
                                        t && (t.scrollIntoViewIfNeeded ? t.scrollIntoViewIfNeeded(!1) : t.scrollIntoView(!1))
                                    },
                                        a = function () {
                                            var e = Pe(o.getState());
                                            if (null !== o.getState().activeItemId && e) {
                                                var n = e.item,
                                                    u = e.itemInputValue,
                                                    a = e.itemUrl,
                                                    c = e.source;
                                                c.onActive(We({
                                                    event: t,
                                                    item: n,
                                                    itemInputValue: u,
                                                    itemUrl: a,
                                                    refresh: r,
                                                    source: c,
                                                    state: o.getState()
                                                }, i))
                                            }
                                        };
                                    t.preventDefault(), !1 === o.getState().isOpen && (n.openOnFocus || Boolean(o.getState().query)) ? qe(We({
                                        event: t,
                                        props: n,
                                        query: o.getState().query,
                                        refresh: r,
                                        store: o
                                    }, i)).then((function () {
                                        o.dispatch(t.key, {
                                            nextActiveItemId: n.defaultActiveItemId
                                        }), a(), setTimeout(u, 0)
                                    })) : (o.dispatch(t.key, {}), a(), u())
                                } else if ("Escape" === t.key) t.preventDefault(), o.dispatch(t.key, null), o.pendingRequests.cancelAll();
                                else if ("Tab" === t.key) o.dispatch("blur", null), o.pendingRequests.cancelAll();
                                else if ("Enter" === t.key) {
                                    if (null === o.getState().activeItemId || o.getState().collections.every((function (e) {
                                        return 0 === e.items.length
                                    }))) {
                                        var c = Ue(n.plugins, o.pendingRequests);
                                        return void (void 0 !== c ? c.then(o.pendingRequests.cancelAll) : n.debug || o.pendingRequests.cancelAll())
                                    }
                                    t.preventDefault();
                                    var l = Pe(o.getState()),
                                        s = l.item,
                                        f = l.itemInputValue,
                                        p = l.itemUrl,
                                        m = l.source;
                                    if (t.metaKey || t.ctrlKey) void 0 !== p && (m.onSelect(We({
                                        event: t,
                                        item: s,
                                        itemInputValue: f,
                                        itemUrl: p,
                                        refresh: r,
                                        source: m,
                                        state: o.getState()
                                    }, i)), n.navigator.navigateNewTab({
                                        itemUrl: p,
                                        item: s,
                                        state: o.getState()
                                    }));
                                    else if (t.shiftKey) void 0 !== p && (m.onSelect(We({
                                        event: t,
                                        item: s,
                                        itemInputValue: f,
                                        itemUrl: p,
                                        refresh: r,
                                        source: m,
                                        state: o.getState()
                                    }, i)), n.navigator.navigateNewWindow({
                                        itemUrl: p,
                                        item: s,
                                        state: o.getState()
                                    }));
                                    else if (t.altKey);
                                    else {
                                        if (void 0 !== p) return m.onSelect(We({
                                            event: t,
                                            item: s,
                                            itemInputValue: f,
                                            itemUrl: p,
                                            refresh: r,
                                            source: m,
                                            state: o.getState()
                                        }, i)), void n.navigator.navigate({
                                            itemUrl: p,
                                            item: s,
                                            state: o.getState()
                                        });
                                        qe(We({
                                            event: t,
                                            nextState: {
                                                isOpen: !1
                                            },
                                            props: n,
                                            query: f,
                                            refresh: r,
                                            store: o
                                        }, i)).then((function () {
                                            m.onSelect(We({
                                                event: t,
                                                item: s,
                                                itemInputValue: f,
                                                itemUrl: p,
                                                refresh: r,
                                                source: m,
                                                state: o.getState()
                                            }, i))
                                        }))
                                    }
                                }
                            }(rt({
                                event: e,
                                props: t,
                                refresh: n,
                                store: r
                            }, o))
                        },
                        onFocus: u,
                        onBlur: z,
                        onClick: function (n) {
                            e.inputElement !== t.environment.document.activeElement || r.getState().isOpen || u(n)
                        }
                    }, s)
                },
                getPanelProps: function (e) {
                    return rt({
                        onMouseDown: function (e) {
                            e.preventDefault()
                        },
                        onMouseLeave: function () {
                            r.dispatch("mouseleave", null)
                        }
                    }, e)
                },
                getListProps: function (e) {
                    var n = e || {},
                        r = n.source,
                        o = it(n, et);
                    return rt({
                        role: "listbox",
                        "aria-labelledby": Re(t.id, "label"),
                        id: Re(t.id, "list", r)
                    }, o)
                },
                getItemProps: function (e) {
                    var i = e.item,
                        u = e.source,
                        a = it(e, tt);
                    return rt({
                        id: Re(t.id, "item-".concat(i.__autocomplete_id), u),
                        role: "option",
                        "aria-selected": r.getState().activeItemId === i.__autocomplete_id,
                        onMouseMove: function (e) {
                            if (i.__autocomplete_id !== r.getState().activeItemId) {
                                r.dispatch("mousemove", i.__autocomplete_id);
                                var t = Pe(r.getState());
                                if (null !== r.getState().activeItemId && t) {
                                    var u = t.item,
                                        a = t.itemInputValue,
                                        c = t.itemUrl,
                                        l = t.source;
                                    l.onActive(rt({
                                        event: e,
                                        item: u,
                                        itemInputValue: a,
                                        itemUrl: c,
                                        refresh: n,
                                        source: l,
                                        state: r.getState()
                                    }, o))
                                }
                            }
                        },
                        onMouseDown: function (e) {
                            e.preventDefault()
                        },
                        onClick: function (e) {
                            var a = u.getItemInputValue({
                                item: i,
                                state: r.getState()
                            }),
                                c = u.getItemUrl({
                                    item: i,
                                    state: r.getState()
                                });
                            (c ? Promise.resolve() : qe(rt({
                                event: e,
                                nextState: {
                                    isOpen: !1
                                },
                                props: t,
                                query: a,
                                refresh: n,
                                store: r
                            }, o))).then((function () {
                                u.onSelect(rt({
                                    event: e,
                                    item: i,
                                    itemInputValue: a,
                                    itemUrl: c,
                                    refresh: n,
                                    source: u,
                                    state: r.getState()
                                }, o))
                            }))
                        }
                    }, a)
                }
            }
        }
        var at = "1.19.0",
            ct = [{
                segment: "autocomplete-core",
                version: at
            }];

        function lt(e) {
            return lt = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (e) {
                return typeof e
            } : function (e) {
                return e && "function" == typeof Symbol && e.constructor === Symbol && e !== Symbol.prototype ? "symbol" : typeof e
            }, lt(e)
        }

        function st(e, t) {
            var n = Object.keys(e);
            if (Object.getOwnPropertySymbols) {
                var r = Object.getOwnPropertySymbols(e);
                t && (r = r.filter((function (t) {
                    return Object.getOwnPropertyDescriptor(e, t).enumerable
                }))), n.push.apply(n, r)
            }
            return n
        }

        function ft(e) {
            for (var t = 1; t < arguments.length; t++) {
                var n = null != arguments[t] ? arguments[t] : {};
                t % 2 ? st(Object(n), !0).forEach((function (t) {
                    pt(e, t, n[t])
                })) : Object.getOwnPropertyDescriptors ? Object.defineProperties(e, Object.getOwnPropertyDescriptors(n)) : st(Object(n)).forEach((function (t) {
                    Object.defineProperty(e, t, Object.getOwnPropertyDescriptor(n, t))
                }))
            }
            return e
        }

        function pt(e, t, n) {
            return (t = function (e) {
                var t = function (e, t) {
                    if ("object" !== lt(e) || null === e) return e;
                    var n = e[Symbol.toPrimitive];
                    if (void 0 !== n) {
                        var r = n.call(e, t || "default");
                        if ("object" !== lt(r)) return r;
                        throw new TypeError("@@toPrimitive must return a primitive value.")
                    }
                    return ("string" === t ? String : Number)(e)
                }(e, "string");
                return "symbol" === lt(t) ? t : String(t)
            }(t)) in e ? Object.defineProperty(e, t, {
                value: n,
                enumerable: !0,
                configurable: !0,
                writable: !0
            }) : e[t] = n, e
        }

        function mt(e) {
            var t, n, r, o, i = e.plugins,
                u = e.options,
                a = null === (t = ((null === (n = u.__autocomplete_metadata) || void 0 === n ? void 0 : n.userAgents) || [])[0]) || void 0 === t ? void 0 : t.segment,
                c = a ? pt({}, a, Object.keys((null === (r = u.__autocomplete_metadata) || void 0 === r ? void 0 : r.options) || {})) : {};
            return {
                plugins: i.map((function (e) {
                    return {
                        name: e.name,
                        options: Object.keys(e.__autocomplete_pluginOptions || [])
                    }
                })),
                options: ft({
                    "autocomplete-core": Object.keys(u)
                }, c),
                ua: ct.concat((null === (o = u.__autocomplete_metadata) || void 0 === o ? void 0 : o.userAgents) || [])
            }
        }

        function vt(e) {
            var t, n = e.state;
            return !1 === n.isOpen || null === n.activeItemId ? null : (null === (t = Pe(n)) || void 0 === t ? void 0 : t.itemInputValue) || null
        }

        function yt(e, t, n, r) {
            if (!n) return null;
            if (e < 0 && (null === t || null !== r && 0 === t)) return n + e;
            var o = (null === t ? -1 : t) + e;
            return o <= -1 || o >= n ? null === r ? null : 0 : o
        }

        function dt(e) {
            return dt = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (e) {
                return typeof e
            } : function (e) {
                return e && "function" == typeof Symbol && e.constructor === Symbol && e !== Symbol.prototype ? "symbol" : typeof e
            }, dt(e)
        }

        function bt(e, t) {
            var n = Object.keys(e);
            if (Object.getOwnPropertySymbols) {
                var r = Object.getOwnPropertySymbols(e);
                t && (r = r.filter((function (t) {
                    return Object.getOwnPropertyDescriptor(e, t).enumerable
                }))), n.push.apply(n, r)
            }
            return n
        }

        function gt(e) {
            for (var t = 1; t < arguments.length; t++) {
                var n = null != arguments[t] ? arguments[t] : {};
                t % 2 ? bt(Object(n), !0).forEach((function (t) {
                    ht(e, t, n[t])
                })) : Object.getOwnPropertyDescriptors ? Object.defineProperties(e, Object.getOwnPropertyDescriptors(n)) : bt(Object(n)).forEach((function (t) {
                    Object.defineProperty(e, t, Object.getOwnPropertyDescriptor(n, t))
                }))
            }
            return e
        }

        function ht(e, t, n) {
            return (t = function (e) {
                var t = function (e, t) {
                    if ("object" !== dt(e) || null === e) return e;
                    var n = e[Symbol.toPrimitive];
                    if (void 0 !== n) {
                        var r = n.call(e, t || "default");
                        if ("object" !== dt(r)) return r;
                        throw new TypeError("@@toPrimitive must return a primitive value.")
                    }
                    return ("string" === t ? String : Number)(e)
                }(e, "string");
                return "symbol" === dt(t) ? t : String(t)
            }(t)) in e ? Object.defineProperty(e, t, {
                value: n,
                enumerable: !0,
                configurable: !0,
                writable: !0
            }) : e[t] = n, e
        }
        var Ot = function (e, t) {
            switch (t.type) {
                case "setActiveItemId":
                case "mousemove":
                    return gt(gt({}, e), {}, {
                        activeItemId: t.payload
                    });
                case "setQuery":
                    return gt(gt({}, e), {}, {
                        query: t.payload,
                        completion: null
                    });
                case "setCollections":
                    return gt(gt({}, e), {}, {
                        collections: t.payload
                    });
                case "setIsOpen":
                    return gt(gt({}, e), {}, {
                        isOpen: t.payload
                    });
                case "setStatus":
                    return gt(gt({}, e), {}, {
                        status: t.payload
                    });
                case "setContext":
                    return gt(gt({}, e), {}, {
                        context: gt(gt({}, e.context), t.payload)
                    });
                case "ArrowDown":
                    var n = gt(gt({}, e), {}, {
                        activeItemId: t.payload.hasOwnProperty("nextActiveItemId") ? t.payload.nextActiveItemId : yt(1, e.activeItemId, $(e), t.props.defaultActiveItemId)
                    });
                    return gt(gt({}, n), {}, {
                        completion: vt({
                            state: n
                        })
                    });
                case "ArrowUp":
                    var r = gt(gt({}, e), {}, {
                        activeItemId: yt(-1, e.activeItemId, $(e), t.props.defaultActiveItemId)
                    });
                    return gt(gt({}, r), {}, {
                        completion: vt({
                            state: r
                        })
                    });
                case "Escape":
                    return e.isOpen ? gt(gt({}, e), {}, {
                        activeItemId: null,
                        isOpen: !1,
                        completion: null
                    }) : gt(gt({}, e), {}, {
                        activeItemId: null,
                        query: "",
                        status: "idle",
                        collections: []
                    });
                case "submit":
                    return gt(gt({}, e), {}, {
                        activeItemId: null,
                        isOpen: !1,
                        status: "idle"
                    });
                case "reset":
                    return gt(gt({}, e), {}, {
                        activeItemId: !0 === t.props.openOnFocus ? t.props.defaultActiveItemId : null,
                        status: "idle",
                        completion: null,
                        query: ""
                    });
                case "focus":
                    return gt(gt({}, e), {}, {
                        activeItemId: t.props.defaultActiveItemId,
                        isOpen: (t.props.openOnFocus || Boolean(e.query)) && t.props.shouldPanelOpen({
                            state: e
                        })
                    });
                case "blur":
                    return t.props.debug ? e : gt(gt({}, e), {}, {
                        isOpen: !1,
                        activeItemId: null
                    });
                case "mouseleave":
                    return gt(gt({}, e), {}, {
                        activeItemId: t.props.defaultActiveItemId
                    });
                default:
                    return "The reducer action ".concat(JSON.stringify(t.type), " is not supported."), e
            }
        };

        function _t(e) {
            return _t = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (e) {
                return typeof e
            } : function (e) {
                return e && "function" == typeof Symbol && e.constructor === Symbol && e !== Symbol.prototype ? "symbol" : typeof e
            }, _t(e)
        }

        function St(e, t) {
            var n = Object.keys(e);
            if (Object.getOwnPropertySymbols) {
                var r = Object.getOwnPropertySymbols(e);
                t && (r = r.filter((function (t) {
                    return Object.getOwnPropertyDescriptor(e, t).enumerable
                }))), n.push.apply(n, r)
            }
            return n
        }

        function jt(e) {
            for (var t = 1; t < arguments.length; t++) {
                var n = null != arguments[t] ? arguments[t] : {};
                t % 2 ? St(Object(n), !0).forEach((function (t) {
                    Pt(e, t, n[t])
                })) : Object.getOwnPropertyDescriptors ? Object.defineProperties(e, Object.getOwnPropertyDescriptors(n)) : St(Object(n)).forEach((function (t) {
                    Object.defineProperty(e, t, Object.getOwnPropertyDescriptor(n, t))
                }))
            }
            return e
        }

        function Pt(e, t, n) {
            return (t = function (e) {
                var t = function (e, t) {
                    if ("object" !== _t(e) || null === e) return e;
                    var n = e[Symbol.toPrimitive];
                    if (void 0 !== n) {
                        var r = n.call(e, t || "default");
                        if ("object" !== _t(r)) return r;
                        throw new TypeError("@@toPrimitive must return a primitive value.")
                    }
                    return ("string" === t ? String : Number)(e)
                }(e, "string");
                return "symbol" === _t(t) ? t : String(t)
            }(t)) in e ? Object.defineProperty(e, t, {
                value: n,
                enumerable: !0,
                configurable: !0,
                writable: !0
            }) : e[t] = n, e
        }

        function wt(e) {
            var t = [],
                n = ie(e, t),
                r = F(Ot, n, (function (e) {
                    var t, r, i = e.prevState,
                        l = e.state;
                    if (n.onStateChange(jt({
                        prevState: i,
                        state: l,
                        refresh: u,
                        navigator: n.navigator
                    }, o)), !c() && null !== (t = l.context) && void 0 !== t && null !== (r = t.algoliaInsightsPlugin) && void 0 !== r && r.__automaticInsights && !1 !== n.insights) {
                        var s = k({
                            __autocomplete_clickAnalytics: !1
                        });
                        n.plugins.push(s), a([s])
                    }
                })),
                o = function (e) {
                    var t = e.store;
                    return {
                        setActiveItemId: function (e) {
                            t.dispatch("setActiveItemId", e)
                        },
                        setQuery: function (e) {
                            t.dispatch("setQuery", e)
                        },
                        setCollections: function (e) {
                            var n = 0,
                                r = e.map((function (e) {
                                    return V(V({}, e), {}, {
                                        items: U(e.items).map((function (e) {
                                            return V(V({}, e), {}, {
                                                __autocomplete_id: n++
                                            })
                                        }))
                                    })
                                }));
                            t.dispatch("setCollections", r)
                        },
                        setIsOpen: function (e) {
                            t.dispatch("setIsOpen", e)
                        },
                        setStatus: function (e) {
                            t.dispatch("setStatus", e)
                        },
                        setContext: function (e) {
                            t.dispatch("setContext", e)
                        }
                    }
                }({
                    store: r
                }),
                i = ut(jt({
                    props: n,
                    refresh: u,
                    store: r,
                    navigator: n.navigator
                }, o));

            function u() {
                return qe(jt({
                    event: new Event("input"),
                    nextState: {
                        isOpen: r.getState().isOpen
                    },
                    props: n,
                    navigator: n.navigator,
                    query: r.getState().query,
                    refresh: u,
                    store: r
                }, o))
            }

            function a(e) {
                e.forEach((function (e) {
                    var r;
                    return null === (r = e.subscribe) || void 0 === r ? void 0 : r.call(e, jt(jt({}, o), {}, {
                        navigator: n.navigator,
                        refresh: u,
                        onSelect: function (e) {
                            t.push({
                                onSelect: e
                            })
                        },
                        onActive: function (e) {
                            t.push({
                                onActive: e
                            })
                        },
                        onResolve: function (e) {
                            t.push({
                                onResolve: e
                            })
                        }
                    }))
                }))
            }

            function c() {
                return n.plugins.some((function (e) {
                    return "aa.algoliaInsightsPlugin" === e.name
                }))
            }
            if (n.insights && !c()) {
                var l = "boolean" == typeof n.insights ? {} : n.insights;
                n.plugins.push(k(l))
            }
            return a(n.plugins),
                function (e) {
                    var t, n, r = e.metadata,
                        o = e.environment;
                    if (null === (t = o.navigator) || void 0 === t || null === (n = t.userAgent) || void 0 === n ? void 0 : n.includes("Algolia Crawler")) {
                        var i = o.document.createElement("meta"),
                            u = o.document.querySelector("head");
                        i.name = "algolia:metadata", setTimeout((function () {
                            i.content = JSON.stringify(r), u.appendChild(i)
                        }), 0)
                    }
                }({
                    metadata: mt({
                        plugins: n.plugins,
                        options: e
                    }),
                    environment: n.environment
                }), jt(jt({
                    refresh: u,
                    navigator: n.navigator
                }, i), o)
        }

        function It(e) {
            return {
                current: e
            }
        }

        function Et(e, t) {
            var n = void 0;
            return function () {
                for (var r = arguments.length, o = new Array(r), i = 0; i < r; i++) o[i] = arguments[i];
                n && clearTimeout(n), n = setTimeout((function () {
                    return e.apply(void 0, o)
                }), t)
            }
        }
        var At = function (e, t, n, r) {
            var o;
            t[0] = 0;
            for (var i = 1; i < t.length; i++) {
                var u = t[i++],
                    a = t[i] ? (t[0] |= u ? 1 : 2, n[t[i++]]) : t[++i];
                3 === u ? r[0] = a : 4 === u ? r[1] = Object.assign(r[1] || {}, a) : 5 === u ? (r[1] = r[1] || {})[t[++i]] = a : 6 === u ? r[1][t[++i]] += a + "" : u ? (o = e.apply(a, At(e, a, n, ["", null])), r.push(o), a[0] ? t[0] |= 2 : (t[i - 2] = 0, t[i] = o)) : r.push(a)
            }
            return r
        },
            Dt = new Map;

        function Ct(e) {
            var t = Dt.get(this);
            return t || (t = new Map, Dt.set(this, t)), (t = At(this, t.get(e) || (t.set(e, t = function (e) {
                for (var t, n, r = 1, o = "", i = "", u = [0], a = function (e) {
                    1 === r && (e || (o = o.replace(/^\s*\n\s*|\s*\n\s*$/g, ""))) ? u.push(0, e, o) : 3 === r && (e || o) ? (u.push(3, e, o), r = 2) : 2 === r && "..." === o && e ? u.push(4, e, 0) : 2 === r && o && !e ? u.push(5, 0, !0, o) : r >= 5 && ((o || !e && 5 === r) && (u.push(r, 0, o, n), r = 6), e && (u.push(r, e, 0, n), r = 6)), o = ""
                }, c = 0; c < e.length; c++) {
                    c && (1 === r && a(), a(c));
                    for (var l = 0; l < e[c].length; l++) t = e[c][l], 1 === r ? "<" === t ? (a(), u = [u], r = 3) : o += t : 4 === r ? "--" === o && ">" === t ? (r = 1, o = "") : o = t + o[0] : i ? t === i ? i = "" : o += t : '"' === t || "'" === t ? i = t : ">" === t ? (a(), r = 1) : r && ("=" === t ? (r = 5, n = o, o = "") : "/" === t && (r < 5 || ">" === e[c][l + 1]) ? (a(), 3 === r && (u = u[0]), r = u, (u = u[0]).push(2, 0, r), r = 0) : " " === t || "\t" === t || "\n" === t || "\r" === t ? (a(), r = 2) : o += t), 3 === r && "!--" === o && (r = 4, u = u[0])
                }
                return a(), u
            }(e)), t), arguments, [])).length > 1 ? t : t[0]
        }
        var kt = function (e) {
            var t = e.environment,
                n = t.document.createElementNS("http://www.w3.org/2000/svg", "svg");
            n.setAttribute("class", "aa-SubmitIcon"), n.setAttribute("viewBox", "0 0 24 24"), n.setAttribute("width", "20"), n.setAttribute("height", "20"), n.setAttribute("fill", "currentColor");
            var r = t.document.createElementNS("http://www.w3.org/2000/svg", "path");
            return r.setAttribute("d", "M16.041 15.856c-0.034 0.026-0.067 0.055-0.099 0.087s-0.060 0.064-0.087 0.099c-1.258 1.213-2.969 1.958-4.855 1.958-1.933 0-3.682-0.782-4.95-2.050s-2.050-3.017-2.050-4.95 0.782-3.682 2.050-4.95 3.017-2.050 4.95-2.050 3.682 0.782 4.95 2.050 2.050 3.017 2.050 4.95c0 1.886-0.745 3.597-1.959 4.856zM21.707 20.293l-3.675-3.675c1.231-1.54 1.968-3.493 1.968-5.618 0-2.485-1.008-4.736-2.636-6.364s-3.879-2.636-6.364-2.636-4.736 1.008-6.364 2.636-2.636 3.879-2.636 6.364 1.008 4.736 2.636 6.364 3.879 2.636 6.364 2.636c2.125 0 4.078-0.737 5.618-1.968l3.675 3.675c0.391 0.391 1.024 0.391 1.414 0s0.391-1.024 0-1.414z"), n.appendChild(r), n
        },
            xt = function (e) {
                var t = e.environment,
                    n = t.document.createElementNS("http://www.w3.org/2000/svg", "svg");
                n.setAttribute("class", "aa-ClearIcon"), n.setAttribute("viewBox", "0 0 24 24"), n.setAttribute("width", "18"), n.setAttribute("height", "18"), n.setAttribute("fill", "currentColor");
                var r = t.document.createElementNS("http://www.w3.org/2000/svg", "path");
                return r.setAttribute("d", "M5.293 6.707l5.293 5.293-5.293 5.293c-0.391 0.391-0.391 1.024 0 1.414s1.024 0.391 1.414 0l5.293-5.293 5.293 5.293c0.391 0.391 1.024 0.391 1.414 0s0.391-1.024 0-1.414l-5.293-5.293 5.293-5.293c0.391-0.391 0.391-1.024 0-1.414s-1.024-0.391-1.414 0l-5.293 5.293-5.293-5.293c-0.391-0.391-1.024-0.391-1.414 0s-0.391 1.024 0 1.414z"), n.appendChild(r), n
            },
            Tt = function (e) {
                var t = e.environment.document.createElementNS("http://www.w3.org/2000/svg", "svg");
                return t.setAttribute("class", "aa-LoadingIcon"), t.setAttribute("viewBox", "0 0 100 100"), t.setAttribute("width", "20"), t.setAttribute("height", "20"), t.innerHTML = '<circle\n  cx="50"\n  cy="50"\n  fill="none"\n  r="35"\n  stroke="currentColor"\n  stroke-dasharray="164.93361431346415 56.97787143782138"\n  stroke-width="6"\n>\n  <animateTransform\n    attributeName="transform"\n    type="rotate"\n    repeatCount="indefinite"\n    dur="1s"\n    values="0 50 50;90 50 50;180 50 50;360 50 50"\n    keyTimes="0;0.40;0.65;1"\n  />\n</circle>', t
            },
            Nt = ["ontouchstart", "ontouchend", "ontouchmove", "ontouchcancel"];

        function Bt(e, t, n) {
            e[t] = null === n ? "" : "number" != typeof n ? n : n + "px"
        }

        function qt(e) {
            this._listeners[e.type](e)
        }

        function Rt(e, t, n) {
            var r, o, i = e[t];
            if ("style" === t)
                if ("string" == typeof n) e.style = n;
                else if (null === n) e.style = "";
                else
                    for (t in n) i && n[t] === i[t] || Bt(e.style, t, n[t]);
            else "o" === t[0] && "n" === t[1] ? (r = t !== (t = t.replace(/Capture$/, "")), ((o = t.toLowerCase()) in e || Nt.includes(o)) && (t = o), t = t.slice(2), e._listeners || (e._listeners = {}), e._listeners[t] = n, n ? i || e.addEventListener(t, qt, r) : e.removeEventListener(t, qt, r)) : "list" !== t && "tagName" !== t && "form" !== t && "type" !== t && "size" !== t && "download" !== t && "href" !== t && t in e ? e[t] = null == n ? "" : n : "function" != typeof n && "dangerouslySetInnerHTML" !== t && (null == n || !1 === n && !/^ar/.test(t) ? e.removeAttribute(t) : e.setAttribute(t, n))
        }

        function Lt(e) {
            switch (e) {
                case "onChange":
                    return "onInput";
                case "onCompositionEnd":
                    return "oncompositionend";
                default:
                    return e
            }
        }

        function Ft(e, t) {
            for (var n in t) Rt(e, Lt(n), t[n])
        }

        function Ut(e, t) {
            for (var n in t) "o" === n[0] && "n" === n[1] || Rt(e, Lt(n), t[n])
        }
        var Mt = ["children"];

        function Ht(e) {
            return function (e) {
                if (Array.isArray(e)) return Vt(e)
            }(e) || function (e) {
                if ("undefined" != typeof Symbol && null != e[Symbol.iterator] || null != e["@@iterator"]) return Array.from(e)
            }(e) || function (e, t) {
                if (!e) return;
                if ("string" == typeof e) return Vt(e, t);
                var n = Object.prototype.toString.call(e).slice(8, -1);
                "Object" === n && e.constructor && (n = e.constructor.name);
                if ("Map" === n || "Set" === n) return Array.from(e);
                if ("Arguments" === n || /^(?:Ui|I)nt(?:8|16|32)(?:Clamped)?Array$/.test(n)) return Vt(e, t)
            }(e) || function () {
                throw new TypeError("Invalid attempt to spread non-iterable instance.\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.")
            }()
        }

        function Vt(e, t) {
            (null == t || t > e.length) && (t = e.length);
            for (var n = 0, r = new Array(t); n < t; n++) r[n] = e[n];
            return r
        }

        function Wt(e, t) {
            if (null == e) return {};
            var n, r, o = function (e, t) {
                if (null == e) return {};
                var n, r, o = {},
                    i = Object.keys(e);
                for (r = 0; r < i.length; r++) n = i[r], t.indexOf(n) >= 0 || (o[n] = e[n]);
                return o
            }(e, t);
            if (Object.getOwnPropertySymbols) {
                var i = Object.getOwnPropertySymbols(e);
                for (r = 0; r < i.length; r++) n = i[r], t.indexOf(n) >= 0 || Object.prototype.propertyIsEnumerable.call(e, n) && (o[n] = e[n])
            }
            return o
        }

        function $t(e) {
            return function (t, n) {
                var r = n.children,
                    o = void 0 === r ? [] : r,
                    i = Wt(n, Mt),
                    u = e.document.createElement(t);
                return Ft(u, i), u.append.apply(u, Ht(o)), u
            }
        }

        function Qt(e) {
            return Qt = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (e) {
                return typeof e
            } : function (e) {
                return e && "function" == typeof Symbol && e.constructor === Symbol && e !== Symbol.prototype ? "symbol" : typeof e
            }, Qt(e)
        }
        var Kt = ["autocompleteScopeApi", "environment", "classNames", "getInputProps", "getInputPropsCore", "isDetached", "state"];

        function zt(e, t) {
            var n = Object.keys(e);
            if (Object.getOwnPropertySymbols) {
                var r = Object.getOwnPropertySymbols(e);
                t && (r = r.filter((function (t) {
                    return Object.getOwnPropertyDescriptor(e, t).enumerable
                }))), n.push.apply(n, r)
            }
            return n
        }

        function Gt(e) {
            for (var t = 1; t < arguments.length; t++) {
                var n = null != arguments[t] ? arguments[t] : {};
                t % 2 ? zt(Object(n), !0).forEach((function (t) {
                    Jt(e, t, n[t])
                })) : Object.getOwnPropertyDescriptors ? Object.defineProperties(e, Object.getOwnPropertyDescriptors(n)) : zt(Object(n)).forEach((function (t) {
                    Object.defineProperty(e, t, Object.getOwnPropertyDescriptor(n, t))
                }))
            }
            return e
        }

        function Jt(e, t, n) {
            return (t = function (e) {
                var t = function (e, t) {
                    if ("object" !== Qt(e) || null === e) return e;
                    var n = e[Symbol.toPrimitive];
                    if (void 0 !== n) {
                        var r = n.call(e, t || "default");
                        if ("object" !== Qt(r)) return r;
                        throw new TypeError("@@toPrimitive must return a primitive value.")
                    }
                    return ("string" === t ? String : Number)(e)
                }(e, "string");
                return "symbol" === Qt(t) ? t : String(t)
            }(t)) in e ? Object.defineProperty(e, t, {
                value: n,
                enumerable: !0,
                configurable: !0,
                writable: !0
            }) : e[t] = n, e
        }

        function Xt(e, t) {
            if (null == e) return {};
            var n, r, o = function (e, t) {
                if (null == e) return {};
                var n, r, o = {},
                    i = Object.keys(e);
                for (r = 0; r < i.length; r++) n = i[r], t.indexOf(n) >= 0 || (o[n] = e[n]);
                return o
            }(e, t);
            if (Object.getOwnPropertySymbols) {
                var i = Object.getOwnPropertySymbols(e);
                for (r = 0; r < i.length; r++) n = i[r], t.indexOf(n) >= 0 || Object.prototype.propertyIsEnumerable.call(e, n) && (o[n] = e[n])
            }
            return o
        }
        var Yt = function (e) {
            var t = e.autocompleteScopeApi,
                n = e.environment,
                r = (e.classNames, e.getInputProps),
                o = e.getInputPropsCore,
                i = e.isDetached,
                u = e.state,
                a = Xt(e, Kt),
                c = $t(n)("input", a),
                l = r(Gt({
                    state: u,
                    props: o({
                        inputElement: c
                    }),
                    inputElement: c
                }, t));
            return Ft(c, Gt(Gt({}, l), {}, {
                onKeyDown: function (e) {
                    i && "Tab" === e.key || l.onKeyDown(e)
                }
            })), c
        };

        function Zt(e) {
            return Zt = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (e) {
                return typeof e
            } : function (e) {
                return e && "function" == typeof Symbol && e.constructor === Symbol && e !== Symbol.prototype ? "symbol" : typeof e
            }, Zt(e)
        }

        function en(e, t) {
            var n = Object.keys(e);
            if (Object.getOwnPropertySymbols) {
                var r = Object.getOwnPropertySymbols(e);
                t && (r = r.filter((function (t) {
                    return Object.getOwnPropertyDescriptor(e, t).enumerable
                }))), n.push.apply(n, r)
            }
            return n
        }

        function tn(e) {
            for (var t = 1; t < arguments.length; t++) {
                var n = null != arguments[t] ? arguments[t] : {};
                t % 2 ? en(Object(n), !0).forEach((function (t) {
                    nn(e, t, n[t])
                })) : Object.getOwnPropertyDescriptors ? Object.defineProperties(e, Object.getOwnPropertyDescriptors(n)) : en(Object(n)).forEach((function (t) {
                    Object.defineProperty(e, t, Object.getOwnPropertyDescriptor(n, t))
                }))
            }
            return e
        }

        function nn(e, t, n) {
            return (t = function (e) {
                var t = function (e, t) {
                    if ("object" !== Zt(e) || null === e) return e;
                    var n = e[Symbol.toPrimitive];
                    if (void 0 !== n) {
                        var r = n.call(e, t || "default");
                        if ("object" !== Zt(r)) return r;
                        throw new TypeError("@@toPrimitive must return a primitive value.")
                    }
                    return ("string" === t ? String : Number)(e)
                }(e, "string");
                return "symbol" === Zt(t) ? t : String(t)
            }(t)) in e ? Object.defineProperty(e, t, {
                value: n,
                enumerable: !0,
                configurable: !0,
                writable: !0
            }) : e[t] = n, e
        }
        var rn, on, un, an, cn, ln, sn, fn, pn, mn, vn, yn = {},
            dn = [],
            bn = /acit|ex(?:s|g|n|p|$)|rph|grid|ows|mnc|ntw|ine[ch]|zoo|^ord|itera/i,
            gn = Array.isArray;

        function hn(e, t) {
            for (var n in t) e[n] = t[n];
            return e
        }

        function On(e) {
            e && e.parentNode && e.parentNode.removeChild(e)
        }

        function _n(e, t, n) {
            var r, o, i, u = {};
            for (i in t) "key" == i ? r = t[i] : "ref" == i ? o = t[i] : u[i] = t[i];
            if (arguments.length > 2 && (u.children = arguments.length > 3 ? rn.call(arguments, 2) : n), "function" == typeof e && null != e.defaultProps)
                for (i in e.defaultProps) null == u[i] && (u[i] = e.defaultProps[i]);
            return Sn(e, u, r, o, null)
        }

        function Sn(e, t, n, r, o) {
            var i = {
                type: e,
                props: t,
                key: n,
                ref: r,
                __k: null,
                __: null,
                __b: 0,
                __e: null,
                __c: null,
                constructor: void 0,
                __v: null == o ? ++un : o,
                __i: -1,
                __u: 0
            };
            return null == o && null != on.vnode && on.vnode(i), i
        }

        function jn(e) {
            return e.children
        }

        function Pn(e, t) {
            this.props = e, this.context = t
        }

        function wn(e, t) {
            if (null == t) return e.__ ? wn(e.__, e.__i + 1) : null;
            for (var n; t < e.__k.length; t++)
                if (null != (n = e.__k[t]) && null != n.__e) return n.__e;
            return "function" == typeof e.type ? wn(e) : null
        }

        function In(e) {
            var t, n;
            if (null != (e = e.__) && null != e.__c) {
                for (e.__e = e.__c.base = null, t = 0; t < e.__k.length; t++)
                    if (null != (n = e.__k[t]) && null != n.__e) {
                        e.__e = e.__c.base = n.__e;
                        break
                    } return In(e)
            }
        }

        function En(e) {
            (!e.__d && (e.__d = !0) && an.push(e) && !An.__r++ || cn != on.debounceRendering) && ((cn = on.debounceRendering) || ln)(An)
        }

        function An() {
            for (var e, t, n, r, o, i, u, a = 1; an.length;) an.length > a && an.sort(sn), e = an.shift(), a = an.length, e.__d && (n = void 0, o = (r = (t = e).__v).__e, i = [], u = [], t.__P && ((n = hn({}, r)).__v = r.__v + 1, on.vnode && on.vnode(n), qn(t.__P, n, r, t.__n, t.__P.namespaceURI, 32 & r.__u ? [o] : null, i, null == o ? wn(r) : o, !!(32 & r.__u), u), n.__v = r.__v, n.__.__k[n.__i] = n, Rn(i, n, u), n.__e != o && In(n)));
            An.__r = 0
        }

        function Dn(e, t, n, r, o, i, u, a, c, l, s) {
            var f, p, m, v, y, d, b = r && r.__k || dn,
                g = t.length;
            for (c = Cn(n, t, b, c, g), f = 0; f < g; f++) null != (m = n.__k[f]) && (p = -1 == m.__i ? yn : b[m.__i] || yn, m.__i = f, d = qn(e, m, p, o, i, u, a, c, l, s), v = m.__e, m.ref && p.ref != m.ref && (p.ref && Un(p.ref, null, m), s.push(m.ref, m.__c || v, m)), null == y && null != v && (y = v), 4 & m.__u || p.__k === m.__k ? c = kn(m, c, e) : "function" == typeof m.type && void 0 !== d ? c = d : v && (c = v.nextSibling), m.__u &= -7);
            return n.__e = y, c
        }

        function Cn(e, t, n, r, o) {
            var i, u, a, c, l, s = n.length,
                f = s,
                p = 0;
            for (e.__k = new Array(o), i = 0; i < o; i++) null != (u = t[i]) && "boolean" != typeof u && "function" != typeof u ? (c = i + p, (u = e.__k[i] = "string" == typeof u || "number" == typeof u || "bigint" == typeof u || u.constructor == String ? Sn(null, u, null, null, null) : gn(u) ? Sn(jn, {
                children: u
            }, null, null, null) : null == u.constructor && u.__b > 0 ? Sn(u.type, u.props, u.key, u.ref ? u.ref : null, u.__v) : u).__ = e, u.__b = e.__b + 1, a = null, -1 != (l = u.__i = xn(u, n, c, f)) && (f--, (a = n[l]) && (a.__u |= 2)), null == a || null == a.__v ? (-1 == l && (o > s ? p-- : o < s && p++), "function" != typeof u.type && (u.__u |= 4)) : l != c && (l == c - 1 ? p-- : l == c + 1 ? p++ : (l > c ? p-- : p++, u.__u |= 4))) : e.__k[i] = null;
            if (f)
                for (i = 0; i < s; i++) null != (a = n[i]) && !(2 & a.__u) && (a.__e == r && (r = wn(a)), Mn(a, a));
            return r
        }

        function kn(e, t, n) {
            var r, o;
            if ("function" == typeof e.type) {
                for (r = e.__k, o = 0; r && o < r.length; o++) r[o] && (r[o].__ = e, t = kn(r[o], t, n));
                return t
            }
            e.__e != t && (t && e.type && !n.contains(t) && (t = wn(e)), n.insertBefore(e.__e, t || null), t = e.__e);
            do {
                t = t && t.nextSibling
            } while (null != t && 8 == t.nodeType);
            return t
        }

        function xn(e, t, n, r) {
            var o, i, u = e.key,
                a = e.type,
                c = t[n];
            if (null === c && null == e.key || c && u == c.key && a == c.type && !(2 & c.__u)) return n;
            if (r > (null == c || 2 & c.__u ? 0 : 1))
                for (o = n - 1, i = n + 1; o >= 0 || i < t.length;) {
                    if (o >= 0) {
                        if ((c = t[o]) && !(2 & c.__u) && u == c.key && a == c.type) return o;
                        o--
                    }
                    if (i < t.length) {
                        if ((c = t[i]) && !(2 & c.__u) && u == c.key && a == c.type) return i;
                        i++
                    }
                }
            return -1
        }

        function Tn(e, t, n) {
            "-" == t[0] ? e.setProperty(t, null == n ? "" : n) : e[t] = null == n ? "" : "number" != typeof n || bn.test(t) ? n : n + "px"
        }

        function Nn(e, t, n, r, o) {
            var i;
            e: if ("style" == t)
                if ("string" == typeof n) e.style.cssText = n;
                else {
                    if ("string" == typeof r && (e.style.cssText = r = ""), r)
                        for (t in r) n && t in n || Tn(e.style, t, "");
                    if (n)
                        for (t in n) r && n[t] == r[t] || Tn(e.style, t, n[t])
                }
            else if ("o" == t[0] && "n" == t[1]) i = t != (t = t.replace(fn, "$1")), t = t.toLowerCase() in e || "onFocusOut" == t || "onFocusIn" == t ? t.toLowerCase().slice(2) : t.slice(2), e.l || (e.l = {}), e.l[t + i] = n, n ? r ? n.u = r.u : (n.u = pn, e.addEventListener(t, i ? vn : mn, i)) : e.removeEventListener(t, i ? vn : mn, i);
            else {
                if ("http://www.w3.org/2000/svg" == o) t = t.replace(/xlink(H|:h)/, "h").replace(/sName$/, "s");
                else if ("width" != t && "height" != t && "href" != t && "list" != t && "form" != t && "tabIndex" != t && "download" != t && "rowSpan" != t && "colSpan" != t && "role" != t && "popover" != t && t in e) try {
                    e[t] = null == n ? "" : n;
                    break e
                } catch (e) { }
                "function" == typeof n || (null == n || !1 === n && "-" != t[4] ? e.removeAttribute(t) : e.setAttribute(t, "popover" == t && 1 == n ? "" : n))
            }
        }

        function Bn(e) {
            return function (t) {
                if (this.l) {
                    var n = this.l[t.type + e];
                    if (null == t.t) t.t = pn++;
                    else if (t.t < n.u) return;
                    return n(on.event ? on.event(t) : t)
                }
            }
        }

        function qn(e, t, n, r, o, i, u, a, c, l) {
            var s, f, p, m, v, y, d, b, g, h, O, _, S, j, P, w, I, E = t.type;
            if (null != t.constructor) return null;
            128 & n.__u && (c = !!(32 & n.__u), i = [a = t.__e = n.__e]), (s = on.__b) && s(t);
            e: if ("function" == typeof E) try {
                if (b = t.props, g = "prototype" in E && E.prototype.render, h = (s = E.contextType) && r[s.__c], O = s ? h ? h.props.value : s.__ : r, n.__c ? d = (f = t.__c = n.__c).__ = f.__E : (g ? t.__c = f = new E(b, O) : (t.__c = f = new Pn(b, O), f.constructor = E, f.render = Hn), h && h.sub(f), f.props = b, f.state || (f.state = {}), f.context = O, f.__n = r, p = f.__d = !0, f.__h = [], f._sb = []), g && null == f.__s && (f.__s = f.state), g && null != E.getDerivedStateFromProps && (f.__s == f.state && (f.__s = hn({}, f.__s)), hn(f.__s, E.getDerivedStateFromProps(b, f.__s))), m = f.props, v = f.state, f.__v = t, p) g && null == E.getDerivedStateFromProps && null != f.componentWillMount && f.componentWillMount(), g && null != f.componentDidMount && f.__h.push(f.componentDidMount);
                else {
                    if (g && null == E.getDerivedStateFromProps && b !== m && null != f.componentWillReceiveProps && f.componentWillReceiveProps(b, O), !f.__e && null != f.shouldComponentUpdate && !1 === f.shouldComponentUpdate(b, f.__s, O) || t.__v == n.__v) {
                        for (t.__v != n.__v && (f.props = b, f.state = f.__s, f.__d = !1), t.__e = n.__e, t.__k = n.__k, t.__k.some((function (e) {
                            e && (e.__ = t)
                        })), _ = 0; _ < f._sb.length; _++) f.__h.push(f._sb[_]);
                        f._sb = [], f.__h.length && u.push(f);
                        break e
                    }
                    null != f.componentWillUpdate && f.componentWillUpdate(b, f.__s, O), g && null != f.componentDidUpdate && f.__h.push((function () {
                        f.componentDidUpdate(m, v, y)
                    }))
                }
                if (f.context = O, f.props = b, f.__P = e, f.__e = !1, S = on.__r, j = 0, g) {
                    for (f.state = f.__s, f.__d = !1, S && S(t), s = f.render(f.props, f.state, f.context), P = 0; P < f._sb.length; P++) f.__h.push(f._sb[P]);
                    f._sb = []
                } else
                    do {
                        f.__d = !1, S && S(t), s = f.render(f.props, f.state, f.context), f.state = f.__s
                    } while (f.__d && ++j < 25);
                f.state = f.__s, null != f.getChildContext && (r = hn(hn({}, r), f.getChildContext())), g && !p && null != f.getSnapshotBeforeUpdate && (y = f.getSnapshotBeforeUpdate(m, v)), w = s, null != s && s.type === jn && null == s.key && (w = Ln(s.props.children)), a = Dn(e, gn(w) ? w : [w], t, n, r, o, i, u, a, c, l), f.base = t.__e, t.__u &= -161, f.__h.length && u.push(f), d && (f.__E = f.__ = null)
            } catch (e) {
                if (t.__v = null, c || null != i)
                    if (e.then) {
                        for (t.__u |= c ? 160 : 128; a && 8 == a.nodeType && a.nextSibling;) a = a.nextSibling;
                        i[i.indexOf(a)] = null, t.__e = a
                    } else
                        for (I = i.length; I--;) On(i[I]);
                else t.__e = n.__e, t.__k = n.__k;
                on.__e(e, t, n)
            } else null == i && t.__v == n.__v ? (t.__k = n.__k, t.__e = n.__e) : a = t.__e = Fn(n.__e, t, n, r, o, i, u, c, l);
            return (s = on.diffed) && s(t), 128 & t.__u ? void 0 : a
        }

        function Rn(e, t, n) {
            for (var r = 0; r < n.length; r++) Un(n[r], n[++r], n[++r]);
            on.__c && on.__c(t, e), e.some((function (t) {
                try {
                    e = t.__h, t.__h = [], e.some((function (e) {
                        e.call(t)
                    }))
                } catch (e) {
                    on.__e(e, t.__v)
                }
            }))
        }

        function Ln(e) {
            return "object" != typeof e || null == e || e.__b && e.__b > 0 ? e : gn(e) ? e.map(Ln) : hn({}, e)
        }

        function Fn(e, t, n, r, o, i, u, a, c) {
            var l, s, f, p, m, v, y, d = n.props,
                b = t.props,
                g = t.type;
            if ("svg" == g ? o = "http://www.w3.org/2000/svg" : "math" == g ? o = "http://www.w3.org/1998/Math/MathML" : o || (o = "http://www.w3.org/1999/xhtml"), null != i)
                for (l = 0; l < i.length; l++)
                    if ((m = i[l]) && "setAttribute" in m == !!g && (g ? m.localName == g : 3 == m.nodeType)) {
                        e = m, i[l] = null;
                        break
                    } if (null == e) {
                        if (null == g) return document.createTextNode(b);
                        e = document.createElementNS(o, g, b.is && b), a && (on.__m && on.__m(t, i), a = !1), i = null
                    }
            if (null == g) d === b || a && e.data == b || (e.data = b);
            else {
                if (i = i && rn.call(e.childNodes), d = n.props || yn, !a && null != i)
                    for (d = {}, l = 0; l < e.attributes.length; l++) d[(m = e.attributes[l]).name] = m.value;
                for (l in d)
                    if (m = d[l], "children" == l);
                    else if ("dangerouslySetInnerHTML" == l) f = m;
                    else if (!(l in b)) {
                        if ("value" == l && "defaultValue" in b || "checked" == l && "defaultChecked" in b) continue;
                        Nn(e, l, null, m, o)
                    }
                for (l in b) m = b[l], "children" == l ? p = m : "dangerouslySetInnerHTML" == l ? s = m : "value" == l ? v = m : "checked" == l ? y = m : a && "function" != typeof m || d[l] === m || Nn(e, l, m, d[l], o);
                if (s) a || f && (s.__html == f.__html || s.__html == e.innerHTML) || (e.innerHTML = s.__html), t.__k = [];
                else if (f && (e.innerHTML = ""), Dn("template" == t.type ? e.content : e, gn(p) ? p : [p], t, n, r, "foreignObject" == g ? "http://www.w3.org/1999/xhtml" : o, i, u, i ? i[0] : n.__k && wn(n, 0), a, c), null != i)
                    for (l = i.length; l--;) On(i[l]);
                a || (l = "value", "progress" == g && null == v ? e.removeAttribute("value") : null != v && (v !== e[l] || "progress" == g && !v || "option" == g && v != d[l]) && Nn(e, l, v, d[l], o), l = "checked", null != y && y != e[l] && Nn(e, l, y, d[l], o))
            }
            return e
        }

        function Un(e, t, n) {
            try {
                if ("function" == typeof e) {
                    var r = "function" == typeof e.__u;
                    r && e.__u(), r && null == t || (e.__u = e(t))
                } else e.current = t
            } catch (e) {
                on.__e(e, n)
            }
        }

        function Mn(e, t, n) {
            var r, o;
            if (on.unmount && on.unmount(e), (r = e.ref) && (r.current && r.current != e.__e || Un(r, null, t)), null != (r = e.__c)) {
                if (r.componentWillUnmount) try {
                    r.componentWillUnmount()
                } catch (e) {
                    on.__e(e, t)
                }
                r.base = r.__P = null
            }
            if (r = e.__k)
                for (o = 0; o < r.length; o++) r[o] && Mn(r[o], t, n || "function" != typeof e.type);
            n || On(e.__e), e.__c = e.__ = e.__e = void 0
        }

        function Hn(e, t, n) {
            return this.constructor(e, n)
        }

        function Vn(e, t, n) {
            var r, o, i, u;
            t == document && (t = document.documentElement), on.__ && on.__(e, t), o = (r = "function" == typeof n) ? null : n && n.__k || t.__k, i = [], u = [], qn(t, e = (!r && n || t).__k = _n(jn, null, [e]), o || yn, yn, t.namespaceURI, !r && n ? [n] : o ? null : t.firstChild ? rn.call(t.childNodes) : null, i, !r && n ? n : o ? o.__e : t.firstChild, r, u), Rn(i, e, u)
        }

        function Wn(e, t) {
            return t.reduce((function (e, t) {
                return e && e[t]
            }), e)
        }
        rn = dn.slice, on = {
            __e: function (e, t, n, r) {
                for (var o, i, u; t = t.__;)
                    if ((o = t.__c) && !o.__) try {
                        if ((i = o.constructor) && null != i.getDerivedStateFromError && (o.setState(i.getDerivedStateFromError(e)), u = o.__d), null != o.componentDidCatch && (o.componentDidCatch(e, r || {}), u = o.__d), u) return o.__E = o
                    } catch (t) {
                        e = t
                    }
                throw e
            }
        }, un = 0, Pn.prototype.setState = function (e, t) {
            var n;
            n = null != this.__s && this.__s != this.state ? this.__s : this.__s = hn({}, this.state), "function" == typeof e && (e = e(hn({}, n), this.props)), e && hn(n, e), null != e && this.__v && (t && this._sb.push(t), En(this))
        }, Pn.prototype.forceUpdate = function (e) {
            this.__v && (this.__e = !0, e && this.__h.push(e), En(this))
        }, Pn.prototype.render = jn, an = [], ln = "function" == typeof Promise ? Promise.prototype.then.bind(Promise.resolve()) : setTimeout, sn = function (e, t) {
            return e.__v.__b - t.__v.__b
        }, An.__r = 0, fn = /(PointerCapture)$|Capture$/i, pn = 0, mn = Bn(!1), vn = Bn(!0);
        var $n = "__aa-highlight__",
            Qn = "__/aa-highlight__";

        function Kn(e) {
            var t = e.highlightedValue.split($n),
                n = t.shift(),
                r = function () {
                    var e = arguments.length > 0 && void 0 !== arguments[0] ? arguments[0] : [];
                    return {
                        get: function () {
                            return e
                        },
                        add: function (t) {
                            var n = e[e.length - 1];
                            (null == n ? void 0 : n.isHighlighted) === t.isHighlighted ? e[e.length - 1] = {
                                value: n.value + t.value,
                                isHighlighted: n.isHighlighted
                            } : e.push(t)
                        }
                    }
                }(n ? [{
                    value: n,
                    isHighlighted: !1
                }] : []);
            return t.forEach((function (e) {
                var t = e.split(Qn);
                r.add({
                    value: t[0],
                    isHighlighted: !0
                }), "" !== t[1] && r.add({
                    value: t[1],
                    isHighlighted: !1
                })
            })), r.get()
        }

        function zn(e) {
            return function (e) {
                if (Array.isArray(e)) return Gn(e)
            }(e) || function (e) {
                if ("undefined" != typeof Symbol && null != e[Symbol.iterator] || null != e["@@iterator"]) return Array.from(e)
            }(e) || function (e, t) {
                if (!e) return;
                if ("string" == typeof e) return Gn(e, t);
                var n = Object.prototype.toString.call(e).slice(8, -1);
                "Object" === n && e.constructor && (n = e.constructor.name);
                if ("Map" === n || "Set" === n) return Array.from(e);
                if ("Arguments" === n || /^(?:Ui|I)nt(?:8|16|32)(?:Clamped)?Array$/.test(n)) return Gn(e, t)
            }(e) || function () {
                throw new TypeError("Invalid attempt to spread non-iterable instance.\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.")
            }()
        }

        function Gn(e, t) {
            (null == t || t > e.length) && (t = e.length);
            for (var n = 0, r = new Array(t); n < t; n++) r[n] = e[n];
            return r
        }

        function Jn(e) {
            var t = e.hit,
                n = e.attribute,
                r = Array.isArray(n) ? n : [n],
                o = Wn(t, ["_highlightResult"].concat(zn(r), ["value"]));
            return "string" != typeof o && (o = Wn(t, r) || ""), Kn({
                highlightedValue: o
            })
        }

        function Xn(e) {
            var t = e.createElement,
                n = e.Fragment;

            function r(e) {
                var r = e.hit,
                    o = e.attribute,
                    i = e.tagName,
                    u = void 0 === i ? "mark" : i;
                return t(n, {}, Jn({
                    hit: r,
                    attribute: o
                }).map((function (e, n) {
                    return e.isHighlighted ? t(u, {
                        key: n
                    }, e.value) : e.value
                })))
            }
            return r.__autocomplete_componentName = "Highlight", r
        }
        var Yn = {
            "&amp;": "&",
            "&lt;": "<",
            "&gt;": ">",
            "&quot;": '"',
            "&#39;": "'"
        },
            Zn = new RegExp(/\w/i),
            er = /&(amp|quot|lt|gt|#39);/g,
            tr = RegExp(er.source);

        function nr(e, t) {
            var n, r, o, i = e[t],
                u = (null === (n = e[t + 1]) || void 0 === n ? void 0 : n.isHighlighted) || !0,
                a = (null === (r = e[t - 1]) || void 0 === r ? void 0 : r.isHighlighted) || !0;
            return Zn.test((o = i.value) && tr.test(o) ? o.replace(er, (function (e) {
                return Yn[e]
            })) : o) || a !== u ? i.isHighlighted : a
        }

        function rr(e) {
            return rr = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (e) {
                return typeof e
            } : function (e) {
                return e && "function" == typeof Symbol && e.constructor === Symbol && e !== Symbol.prototype ? "symbol" : typeof e
            }, rr(e)
        }

        function or(e, t) {
            var n = Object.keys(e);
            if (Object.getOwnPropertySymbols) {
                var r = Object.getOwnPropertySymbols(e);
                t && (r = r.filter((function (t) {
                    return Object.getOwnPropertyDescriptor(e, t).enumerable
                }))), n.push.apply(n, r)
            }
            return n
        }

        function ir(e) {
            for (var t = 1; t < arguments.length; t++) {
                var n = null != arguments[t] ? arguments[t] : {};
                t % 2 ? or(Object(n), !0).forEach((function (t) {
                    ur(e, t, n[t])
                })) : Object.getOwnPropertyDescriptors ? Object.defineProperties(e, Object.getOwnPropertyDescriptors(n)) : or(Object(n)).forEach((function (t) {
                    Object.defineProperty(e, t, Object.getOwnPropertyDescriptor(n, t))
                }))
            }
            return e
        }

        function ur(e, t, n) {
            return (t = function (e) {
                var t = function (e, t) {
                    if ("object" !== rr(e) || null === e) return e;
                    var n = e[Symbol.toPrimitive];
                    if (void 0 !== n) {
                        var r = n.call(e, t || "default");
                        if ("object" !== rr(r)) return r;
                        throw new TypeError("@@toPrimitive must return a primitive value.")
                    }
                    return ("string" === t ? String : Number)(e)
                }(e, "string");
                return "symbol" === rr(t) ? t : String(t)
            }(t)) in e ? Object.defineProperty(e, t, {
                value: n,
                enumerable: !0,
                configurable: !0,
                writable: !0
            }) : e[t] = n, e
        }

        function ar(e) {
            return e.some((function (e) {
                return e.isHighlighted
            })) ? e.map((function (t, n) {
                return ir(ir({}, t), {}, {
                    isHighlighted: !nr(e, n)
                })
            })) : e.map((function (e) {
                return ir(ir({}, e), {}, {
                    isHighlighted: !1
                })
            }))
        }

        function cr(e) {
            var t = e.createElement,
                n = e.Fragment;

            function r(e) {
                var r, o = e.hit,
                    i = e.attribute,
                    u = e.tagName,
                    a = void 0 === u ? "mark" : u;
                return t(n, {}, (r = {
                    hit: o,
                    attribute: i
                }, ar(Jn(r))).map((function (e, n) {
                    return e.isHighlighted ? t(a, {
                        key: n
                    }, e.value) : e.value
                })))
            }
            return r.__autocomplete_componentName = "ReverseHighlight", r
        }

        function lr(e) {
            return function (e) {
                if (Array.isArray(e)) return sr(e)
            }(e) || function (e) {
                if ("undefined" != typeof Symbol && null != e[Symbol.iterator] || null != e["@@iterator"]) return Array.from(e)
            }(e) || function (e, t) {
                if (!e) return;
                if ("string" == typeof e) return sr(e, t);
                var n = Object.prototype.toString.call(e).slice(8, -1);
                "Object" === n && e.constructor && (n = e.constructor.name);
                if ("Map" === n || "Set" === n) return Array.from(e);
                if ("Arguments" === n || /^(?:Ui|I)nt(?:8|16|32)(?:Clamped)?Array$/.test(n)) return sr(e, t)
            }(e) || function () {
                throw new TypeError("Invalid attempt to spread non-iterable instance.\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.")
            }()
        }

        function sr(e, t) {
            (null == t || t > e.length) && (t = e.length);
            for (var n = 0, r = new Array(t); n < t; n++) r[n] = e[n];
            return r
        }

        function fr(e) {
            var t = e.hit,
                n = e.attribute,
                r = Array.isArray(n) ? n : [n],
                o = Wn(t, ["_snippetResult"].concat(lr(r), ["value"]));
            return "string" != typeof o && (o = Wn(t, r) || ""), Kn({
                highlightedValue: o
            })
        }

        function pr(e) {
            var t = e.createElement,
                n = e.Fragment;

            function r(e) {
                var r, o = e.hit,
                    i = e.attribute,
                    u = e.tagName,
                    a = void 0 === u ? "mark" : u;
                return t(n, {}, (r = {
                    hit: o,
                    attribute: i
                }, ar(fr(r))).map((function (e, n) {
                    return e.isHighlighted ? t(a, {
                        key: n
                    }, e.value) : e.value
                })))
            }
            return r.__autocomplete_componentName = "ReverseSnippet", r
        }

        function mr(e) {
            var t = e.createElement,
                n = e.Fragment;

            function r(e) {
                var r = e.hit,
                    o = e.attribute,
                    i = e.tagName,
                    u = void 0 === i ? "mark" : i;
                return t(n, {}, fr({
                    hit: r,
                    attribute: o
                }).map((function (e, n) {
                    return e.isHighlighted ? t(u, {
                        key: n
                    }, e.value) : e.value
                })))
            }
            return r.__autocomplete_componentName = "Snippet", r
        }

        function vr(e, t) {
            if ("string" == typeof t) {
                var n = e.document.querySelector(t);
                return "The element ".concat(JSON.stringify(t), " is not in the document."), n
            }
            return t
        }

        function yr() {
            for (var e = arguments.length, t = new Array(e), n = 0; n < e; n++) t[n] = arguments[n];
            return t.reduce((function (e, t) {
                return Object.keys(t).forEach((function (n) {
                    var r = e[n],
                        o = t[n];
                    r !== o && (e[n] = [r, o].filter(Boolean).join(" "))
                })), e
            }), {})
        }

        function dr(e) {
            return dr = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (e) {
                return typeof e
            } : function (e) {
                return e && "function" == typeof Symbol && e.constructor === Symbol && e !== Symbol.prototype ? "symbol" : typeof e
            }, dr(e)
        }
        var br = ["classNames", "container", "getEnvironmentProps", "getFormProps", "getInputProps", "getItemProps", "getLabelProps", "getListProps", "getPanelProps", "getRootProps", "panelContainer", "panelPlacement", "render", "renderNoResults", "renderer", "detachedMediaQuery", "components", "translations"];

        function gr(e, t) {
            var n = Object.keys(e);
            if (Object.getOwnPropertySymbols) {
                var r = Object.getOwnPropertySymbols(e);
                t && (r = r.filter((function (t) {
                    return Object.getOwnPropertyDescriptor(e, t).enumerable
                }))), n.push.apply(n, r)
            }
            return n
        }

        function hr(e) {
            for (var t = 1; t < arguments.length; t++) {
                var n = null != arguments[t] ? arguments[t] : {};
                t % 2 ? gr(Object(n), !0).forEach((function (t) {
                    Or(e, t, n[t])
                })) : Object.getOwnPropertyDescriptors ? Object.defineProperties(e, Object.getOwnPropertyDescriptors(n)) : gr(Object(n)).forEach((function (t) {
                    Object.defineProperty(e, t, Object.getOwnPropertyDescriptor(n, t))
                }))
            }
            return e
        }

        function Or(e, t, n) {
            return (t = function (e) {
                var t = function (e, t) {
                    if ("object" !== dr(e) || null === e) return e;
                    var n = e[Symbol.toPrimitive];
                    if (void 0 !== n) {
                        var r = n.call(e, t || "default");
                        if ("object" !== dr(r)) return r;
                        throw new TypeError("@@toPrimitive must return a primitive value.")
                    }
                    return ("string" === t ? String : Number)(e)
                }(e, "string");
                return "symbol" === dr(t) ? t : String(t)
            }(t)) in e ? Object.defineProperty(e, t, {
                value: n,
                enumerable: !0,
                configurable: !0,
                writable: !0
            }) : e[t] = n, e
        }

        function _r(e, t) {
            if (null == e) return {};
            var n, r, o = function (e, t) {
                if (null == e) return {};
                var n, r, o = {},
                    i = Object.keys(e);
                for (r = 0; r < i.length; r++) n = i[r], t.indexOf(n) >= 0 || (o[n] = e[n]);
                return o
            }(e, t);
            if (Object.getOwnPropertySymbols) {
                var i = Object.getOwnPropertySymbols(e);
                for (r = 0; r < i.length; r++) n = i[r], t.indexOf(n) >= 0 || Object.prototype.propertyIsEnumerable.call(e, n) && (o[n] = e[n])
            }
            return o
        }
        var Sr = {
            clearButton: "aa-ClearButton",
            detachedCancelButton: "aa-DetachedCancelButton",
            detachedContainer: "aa-DetachedContainer",
            detachedFormContainer: "aa-DetachedFormContainer",
            detachedOverlay: "aa-DetachedOverlay",
            detachedSearchButton: "aa-DetachedSearchButton",
            detachedSearchButtonIcon: "aa-DetachedSearchButtonIcon",
            detachedSearchButtonPlaceholder: "aa-DetachedSearchButtonPlaceholder",
            detachedSearchButtonQuery: "aa-DetachedSearchButtonQuery",
            form: "aa-Form",
            input: "aa-Input",
            inputWrapper: "aa-InputWrapper",
            inputWrapperPrefix: "aa-InputWrapperPrefix",
            inputWrapperSuffix: "aa-InputWrapperSuffix",
            item: "aa-Item",
            label: "aa-Label",
            list: "aa-List",
            loadingIndicator: "aa-LoadingIndicator",
            panel: "aa-Panel",
            panelLayout: "aa-PanelLayout aa-Panel--scrollable",
            root: "aa-Autocomplete",
            source: "aa-Source",
            sourceFooter: "aa-SourceFooter",
            sourceHeader: "aa-SourceHeader",
            sourceNoResults: "aa-SourceNoResults",
            submitButton: "aa-SubmitButton"
        },
            jr = function (e, t) {
                var n = e.children;
                (0, e.render)(n, t)
            },
            Pr = {
                createElement: _n,
                Fragment: jn,
                render: Vn
            };

        function wr(e) {
            var t = e.panelPlacement,
                n = e.container,
                r = e.form,
                o = e.environment,
                i = n.getBoundingClientRect(),
                u = (o.pageYOffset || o.document.documentElement.scrollTop || o.document.body.scrollTop || 0) + i.top + i.height;
            switch (t) {
                case "start":
                    return {
                        top: u, left: i.left
                    };
                case "end":
                    return {
                        top: u, right: o.document.documentElement.clientWidth - (i.left + i.width)
                    };
                case "full-width":
                    return {
                        top: u, left: 0, right: 0, width: "unset", maxWidth: "unset"
                    };
                case "input-wrapper-width":
                    var a = r.getBoundingClientRect();
                    return {
                        top: u, left: a.left, right: o.document.documentElement.clientWidth - (a.left + a.width), width: "unset", maxWidth: "unset"
                    };
                default:
                    throw new Error("[Autocomplete] The `panelPlacement` value ".concat(JSON.stringify(t), " is not valid."))
            }
        }

        function Ir(e) {
            return Ir = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (e) {
                return typeof e
            } : function (e) {
                return e && "function" == typeof Symbol && e.constructor === Symbol && e !== Symbol.prototype ? "symbol" : typeof e
            }, Ir(e)
        }

        function Er() {
            return Er = Object.assign ? Object.assign.bind() : function (e) {
                for (var t = 1; t < arguments.length; t++) {
                    var n = arguments[t];
                    for (var r in n) Object.prototype.hasOwnProperty.call(n, r) && (e[r] = n[r])
                }
                return e
            }, Er.apply(this, arguments)
        }

        function Ar(e, t) {
            var n = Object.keys(e);
            if (Object.getOwnPropertySymbols) {
                var r = Object.getOwnPropertySymbols(e);
                t && (r = r.filter((function (t) {
                    return Object.getOwnPropertyDescriptor(e, t).enumerable
                }))), n.push.apply(n, r)
            }
            return n
        }

        function Dr(e) {
            for (var t = 1; t < arguments.length; t++) {
                var n = null != arguments[t] ? arguments[t] : {};
                t % 2 ? Ar(Object(n), !0).forEach((function (t) {
                    Cr(e, t, n[t])
                })) : Object.getOwnPropertyDescriptors ? Object.defineProperties(e, Object.getOwnPropertyDescriptors(n)) : Ar(Object(n)).forEach((function (t) {
                    Object.defineProperty(e, t, Object.getOwnPropertyDescriptor(n, t))
                }))
            }
            return e
        }

        function Cr(e, t, n) {
            return (t = function (e) {
                var t = function (e, t) {
                    if ("object" !== Ir(e) || null === e) return e;
                    var n = e[Symbol.toPrimitive];
                    if (void 0 !== n) {
                        var r = n.call(e, t || "default");
                        if ("object" !== Ir(r)) return r;
                        throw new TypeError("@@toPrimitive must return a primitive value.")
                    }
                    return ("string" === t ? String : Number)(e)
                }(e, "string");
                return "symbol" === Ir(t) ? t : String(t)
            }(t)) in e ? Object.defineProperty(e, t, {
                value: n,
                enumerable: !0,
                configurable: !0,
                writable: !0
            }) : e[t] = n, e
        }
        var kr = [{
            segment: "autocomplete-js",
            version: at
        }];

        function xr(e) {
            return function (e) {
                if (Array.isArray(e)) return Tr(e)
            }(e) || function (e) {
                if ("undefined" != typeof Symbol && null != e[Symbol.iterator] || null != e["@@iterator"]) return Array.from(e)
            }(e) || function (e, t) {
                if (!e) return;
                if ("string" == typeof e) return Tr(e, t);
                var n = Object.prototype.toString.call(e).slice(8, -1);
                "Object" === n && e.constructor && (n = e.constructor.name);
                if ("Map" === n || "Set" === n) return Array.from(e);
                if ("Arguments" === n || /^(?:Ui|I)nt(?:8|16|32)(?:Clamped)?Array$/.test(n)) return Tr(e, t)
            }(e) || function () {
                throw new TypeError("Invalid attempt to spread non-iterable instance.\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.")
            }()
        }

        function Tr(e, t) {
            (null == t || t > e.length) && (t = e.length);
            for (var n = 0, r = new Array(t); n < t; n++) r[n] = e[n];
            return r
        }

        function Nr(e) {
            return Nr = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (e) {
                return typeof e
            } : function (e) {
                return e && "function" == typeof Symbol && e.constructor === Symbol && e !== Symbol.prototype ? "symbol" : typeof e
            }, Nr(e)
        }
        var Br = function (e) {
            return e && "object" === Nr(e) && "[object Object]" === Object.prototype.toString.call(e)
        };

        function qr() {
            for (var e = arguments.length, t = new Array(e), n = 0; n < e; n++) t[n] = arguments[n];
            return t.reduce((function (e, t) {
                return Object.keys(t).forEach((function (n) {
                    var r = e[n],
                        o = t[n];
                    Array.isArray(r) && Array.isArray(o) ? e[n] = r.concat.apply(r, xr(o)) : Br(r) && Br(o) ? e[n] = qr(r, o) : e[n] = o
                })), e
            }), {})
        }

        function Rr(e) {
            return Rr = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (e) {
                return typeof e
            } : function (e) {
                return e && "function" == typeof Symbol && e.constructor === Symbol && e !== Symbol.prototype ? "symbol" : typeof e
            }, Rr(e)
        }

        function Lr(e, t) {
            var n = Object.keys(e);
            if (Object.getOwnPropertySymbols) {
                var r = Object.getOwnPropertySymbols(e);
                t && (r = r.filter((function (t) {
                    return Object.getOwnPropertyDescriptor(e, t).enumerable
                }))), n.push.apply(n, r)
            }
            return n
        }

        function Fr(e) {
            for (var t = 1; t < arguments.length; t++) {
                var n = null != arguments[t] ? arguments[t] : {};
                t % 2 ? Lr(Object(n), !0).forEach((function (t) {
                    Ur(e, t, n[t])
                })) : Object.getOwnPropertyDescriptors ? Object.defineProperties(e, Object.getOwnPropertyDescriptors(n)) : Lr(Object(n)).forEach((function (t) {
                    Object.defineProperty(e, t, Object.getOwnPropertyDescriptor(n, t))
                }))
            }
            return e
        }

        function Ur(e, t, n) {
            return (t = function (e) {
                var t = function (e, t) {
                    if ("object" !== Rr(e) || null === e) return e;
                    var n = e[Symbol.toPrimitive];
                    if (void 0 !== n) {
                        var r = n.call(e, t || "default");
                        if ("object" !== Rr(r)) return r;
                        throw new TypeError("@@toPrimitive must return a primitive value.")
                    }
                    return ("string" === t ? String : Number)(e)
                }(e, "string");
                return "symbol" === Rr(t) ? t : String(t)
            }(t)) in e ? Object.defineProperty(e, t, {
                value: n,
                enumerable: !0,
                configurable: !0,
                writable: !0
            }) : e[t] = n, e
        }

        function Mr(e, t) {
            return function (e) {
                if (Array.isArray(e)) return e
            }(e) || function (e, t) {
                var n = null == e ? null : "undefined" != typeof Symbol && e[Symbol.iterator] || e["@@iterator"];
                if (null != n) {
                    var r, o, i, u, a = [],
                        c = !0,
                        l = !1;
                    try {
                        if (i = (n = n.call(e)).next, 0 === t) {
                            if (Object(n) !== n) return;
                            c = !1
                        } else
                            for (; !(c = (r = i.call(n)).done) && (a.push(r.value), a.length !== t); c = !0);
                    } catch (e) {
                        l = !0, o = e
                    } finally {
                        try {
                            if (!c && null != n.return && (u = n.return(), Object(u) !== u)) return
                        } finally {
                            if (l) throw o
                        }
                    }
                    return a
                }
            }(e, t) || function (e, t) {
                if (!e) return;
                if ("string" == typeof e) return Hr(e, t);
                var n = Object.prototype.toString.call(e).slice(8, -1);
                "Object" === n && e.constructor && (n = e.constructor.name);
                if ("Map" === n || "Set" === n) return Array.from(e);
                if ("Arguments" === n || /^(?:Ui|I)nt(?:8|16|32)(?:Clamped)?Array$/.test(n)) return Hr(e, t)
            }(e, t) || function () {
                throw new TypeError("Invalid attempt to destructure non-iterable instance.\nIn order to be iterable, non-array objects must have a [Symbol.iterator]() method.")
            }()
        }

        function Hr(e, t) {
            (null == t || t > e.length) && (t = e.length);
            for (var n = 0, r = new Array(t); n < t; n++) r[n] = e[n];
            return r
        }
        var Vr = ["components"];

        function Wr(e) {
            return Wr = "function" == typeof Symbol && "symbol" == typeof Symbol.iterator ? function (e) {
                return typeof e
            } : function (e) {
                return e && "function" == typeof Symbol && e.constructor === Symbol && e !== Symbol.prototype ? "symbol" : typeof e
            }, Wr(e)
        }

        function $r(e, t) {
            if (null == e) return {};
            var n, r, o = function (e, t) {
                if (null == e) return {};
                var n, r, o = {},
                    i = Object.keys(e);
                for (r = 0; r < i.length; r++) n = i[r], t.indexOf(n) >= 0 || (o[n] = e[n]);
                return o
            }(e, t);
            if (Object.getOwnPropertySymbols) {
                var i = Object.getOwnPropertySymbols(e);
                for (r = 0; r < i.length; r++) n = i[r], t.indexOf(n) >= 0 || Object.prototype.propertyIsEnumerable.call(e, n) && (o[n] = e[n])
            }
            return o
        }

        function Qr(e, t) {
            var n = Object.keys(e);
            if (Object.getOwnPropertySymbols) {
                var r = Object.getOwnPropertySymbols(e);
                t && (r = r.filter((function (t) {
                    return Object.getOwnPropertyDescriptor(e, t).enumerable
                }))), n.push.apply(n, r)
            }
            return n
        }

        function Kr(e) {
            for (var t = 1; t < arguments.length; t++) {
                var n = null != arguments[t] ? arguments[t] : {};
                t % 2 ? Qr(Object(n), !0).forEach((function (t) {
                    zr(e, t, n[t])
                })) : Object.getOwnPropertyDescriptors ? Object.defineProperties(e, Object.getOwnPropertyDescriptors(n)) : Qr(Object(n)).forEach((function (t) {
                    Object.defineProperty(e, t, Object.getOwnPropertyDescriptor(n, t))
                }))
            }
            return e
        }

        function zr(e, t, n) {
            return (t = function (e) {
                var t = function (e, t) {
                    if ("object" !== Wr(e) || null === e) return e;
                    var n = e[Symbol.toPrimitive];
                    if (void 0 !== n) {
                        var r = n.call(e, t || "default");
                        if ("object" !== Wr(r)) return r;
                        throw new TypeError("@@toPrimitive must return a primitive value.")
                    }
                    return ("string" === t ? String : Number)(e)
                }(e, "string");
                return "symbol" === Wr(t) ? t : String(t)
            }(t)) in e ? Object.defineProperty(e, t, {
                value: n,
                enumerable: !0,
                configurable: !0,
                writable: !0
            }) : e[t] = n, e
        }

        function Gr(e) {
            var t, n = function () {
                var e = [],
                    t = [];

                function n(n) {
                    e.push(n);
                    var r = n();
                    t.push(r)
                }
                return {
                    runEffect: n,
                    cleanupEffects: function () {
                        var e = t;
                        t = [], e.forEach((function (e) {
                            e()
                        }))
                    },
                    runEffects: function () {
                        var t = e;
                        e = [], t.forEach((function (e) {
                            n(e)
                        }))
                    }
                }
            }(),
                r = n.runEffect,
                o = n.cleanupEffects,
                i = n.runEffects,
                u = (t = [], {
                    reactive: function (e) {
                        var n = e(),
                            r = {
                                _fn: e,
                                _ref: {
                                    current: n
                                },
                                get value() {
                                    return this._ref.current
                                },
                                set value(e) {
                                    this._ref.current = e
                                }
                            };
                        return t.push(r), r
                    },
                    runReactives: function () {
                        t.forEach((function (e) {
                            e._ref.current = e._fn()
                        }))
                    }
                }),
                a = u.reactive,
                c = u.runReactives,
                l = It(!1),
                s = It(e),
                f = It(void 0),
                p = a((function () {
                    return function (e) {
                        var t, n = e.classNames,
                            r = e.container,
                            o = e.getEnvironmentProps,
                            i = e.getFormProps,
                            u = e.getInputProps,
                            a = e.getItemProps,
                            c = e.getLabelProps,
                            l = e.getListProps,
                            s = e.getPanelProps,
                            f = e.getRootProps,
                            p = e.panelContainer,
                            m = e.panelPlacement,
                            v = e.render,
                            y = e.renderNoResults,
                            d = e.renderer,
                            b = e.detachedMediaQuery,
                            g = e.components,
                            h = e.translations,
                            O = _r(e, br),
                            _ = "undefined" != typeof window ? window : {},
                            S = vr(_, r);
                        S.tagName;
                        var j = hr(hr({}, Pr), d),
                            P = {
                                Highlight: Xn(j),
                                ReverseHighlight: cr(j),
                                ReverseSnippet: pr(j),
                                Snippet: mr(j)
                            };
                        return {
                            renderer: {
                                classNames: yr(Sr, null != n ? n : {}),
                                container: S,
                                getEnvironmentProps: null != o ? o : function (e) {
                                    return e.props
                                },
                                getFormProps: null != i ? i : function (e) {
                                    return e.props
                                },
                                getInputProps: null != u ? u : function (e) {
                                    return e.props
                                },
                                getItemProps: null != a ? a : function (e) {
                                    return e.props
                                },
                                getLabelProps: null != c ? c : function (e) {
                                    return e.props
                                },
                                getListProps: null != l ? l : function (e) {
                                    return e.props
                                },
                                getPanelProps: null != s ? s : function (e) {
                                    return e.props
                                },
                                getRootProps: null != f ? f : function (e) {
                                    return e.props
                                },
                                panelContainer: p ? vr(_, p) : _.document.body,
                                panelPlacement: null != m ? m : "input-wrapper-width",
                                render: null != v ? v : jr,
                                renderNoResults: y,
                                renderer: j,
                                detachedMediaQuery: null != b ? b : getComputedStyle(_.document.documentElement).getPropertyValue("--aa-detached-media-query"),
                                components: hr(hr({}, P), g),
                                translations: hr(hr({}, {
                                    clearButtonTitle: "Clear",
                                    detachedCancelButtonText: "Cancel",
                                    detachedSearchButtonTitle: "Search",
                                    submitButtonTitle: "Submit"
                                }), h)
                            },
                            core: hr(hr({}, O), {}, {
                                id: null !== (t = O.id) && void 0 !== t ? t : K(),
                                environment: _
                            })
                        }
                    }(s.current)
                })),
                m = a((function () {
                    return p.value.core.environment.matchMedia(p.value.renderer.detachedMediaQuery).matches
                })),
                v = a((function () {
                    return wt(Kr(Kr({}, p.value.core), {}, {
                        onStateChange: function (e) {
                            var t, n, r;
                            l.current = e.state.collections.some((function (e) {
                                return e.source.templates.noResults
                            })), null === (t = f.current) || void 0 === t || t.call(f, e), null === (n = (r = p.value.core).onStateChange) || void 0 === n || n.call(r, e)
                        },
                        shouldPanelOpen: s.current.shouldPanelOpen || function (e) {
                            var t = e.state;
                            if (m.value) return !0;
                            var n = $(t) > 0;
                            if (!p.value.core.openOnFocus && !t.query) return n;
                            var r = Boolean(l.current || p.value.renderer.renderNoResults);
                            return !n && r || n
                        },
                        __autocomplete_metadata: {
                            userAgents: kr,
                            options: e
                        }
                    }))
                })),
                y = It(Kr({
                    collections: [],
                    completion: null,
                    context: {},
                    isOpen: !1,
                    query: "",
                    activeItemId: null,
                    status: "idle"
                }, p.value.core.initialState)),
                d = {
                    getEnvironmentProps: p.value.renderer.getEnvironmentProps,
                    getFormProps: p.value.renderer.getFormProps,
                    getInputProps: p.value.renderer.getInputProps,
                    getItemProps: p.value.renderer.getItemProps,
                    getLabelProps: p.value.renderer.getLabelProps,
                    getListProps: p.value.renderer.getListProps,
                    getPanelProps: p.value.renderer.getPanelProps,
                    getRootProps: p.value.renderer.getRootProps
                },
                b = {
                    setActiveItemId: v.value.setActiveItemId,
                    setQuery: v.value.setQuery,
                    setCollections: v.value.setCollections,
                    setIsOpen: v.value.setIsOpen,
                    setStatus: v.value.setStatus,
                    setContext: v.value.setContext,
                    refresh: v.value.refresh,
                    navigator: v.value.navigator
                },
                g = a((function () {
                    return Ct.bind(p.value.renderer.renderer.createElement)
                })),
                h = a((function () {
                    return function (e) {
                        var t = e.autocomplete,
                            n = e.autocompleteScopeApi,
                            r = e.classNames,
                            o = e.environment,
                            i = e.isDetached,
                            u = e.placeholder,
                            a = void 0 === u ? "Search" : u,
                            c = e.propGetters,
                            l = e.setIsModalOpen,
                            s = e.state,
                            f = e.translations,
                            p = $t(o),
                            m = c.getRootProps(tn({
                                state: s,
                                props: t.getRootProps({})
                            }, n)),
                            v = p("div", tn({
                                class: r.root
                            }, m)),
                            y = p("div", {
                                class: r.detachedContainer,
                                onMouseDown: function (e) {
                                    e.stopPropagation()
                                }
                            }),
                            d = p("div", {
                                class: r.detachedOverlay,
                                children: [y],
                                onMouseDown: function () {
                                    l(!1), t.setIsOpen(!1)
                                }
                            }),
                            b = c.getLabelProps(tn({
                                state: s,
                                props: t.getLabelProps({})
                            }, n)),
                            g = p("button", {
                                class: r.submitButton,
                                type: "submit",
                                title: f.submitButtonTitle,
                                children: [kt({
                                    environment: o
                                })]
                            }),
                            h = p("label", tn({
                                class: r.label,
                                children: [g],
                                ariaLabel: f.submitButtonTitle
                            }, b)),
                            O = p("button", {
                                class: r.clearButton,
                                type: "reset",
                                title: f.clearButtonTitle,
                                children: [xt({
                                    environment: o
                                })]
                            }),
                            _ = p("div", {
                                class: r.loadingIndicator,
                                children: [Tt({
                                    environment: o
                                })]
                            }),
                            S = Yt({
                                class: r.input,
                                environment: o,
                                state: s,
                                getInputProps: c.getInputProps,
                                getInputPropsCore: t.getInputProps,
                                autocompleteScopeApi: n,
                                isDetached: i
                            }),
                            j = p("div", {
                                class: r.inputWrapperPrefix,
                                children: [h, _]
                            }),
                            P = p("div", {
                                class: r.inputWrapperSuffix,
                                children: [O]
                            }),
                            w = p("div", {
                                class: r.inputWrapper,
                                children: [S]
                            }),
                            I = c.getFormProps(tn({
                                state: s,
                                props: t.getFormProps({
                                    inputElement: S
                                })
                            }, n)),
                            E = p("form", tn({
                                class: r.form,
                                children: [j, w, P]
                            }, I)),
                            A = c.getPanelProps(tn({
                                state: s,
                                props: t.getPanelProps({})
                            }, n)),
                            D = p("div", tn({
                                class: r.panel
                            }, A)),
                            C = p("div", {
                                class: r.detachedSearchButtonQuery,
                                textContent: s.query
                            }),
                            k = p("div", {
                                class: r.detachedSearchButtonPlaceholder,
                                hidden: Boolean(s.query),
                                textContent: a
                            });
                        if (i) {
                            var x = p("div", {
                                class: r.detachedSearchButtonIcon,
                                children: [kt({
                                    environment: o
                                })]
                            }),
                                T = p("button", {
                                    type: "button",
                                    class: r.detachedSearchButton,
                                    title: f.detachedSearchButtonTitle,
                                    id: b.id,
                                    onClick: function () {
                                        l(!0)
                                    },
                                    children: [x, k, C]
                                }),
                                N = p("button", {
                                    type: "button",
                                    class: r.detachedCancelButton,
                                    textContent: f.detachedCancelButtonText,
                                    onTouchStart: function (e) {
                                        e.stopPropagation()
                                    },
                                    onClick: function () {
                                        t.setIsOpen(!1), l(!1)
                                    }
                                }),
                                B = p("div", {
                                    class: r.detachedFormContainer,
                                    children: [E, N]
                                });
                            y.appendChild(B), v.appendChild(T)
                        } else v.appendChild(E);
                        return {
                            detachedContainer: y,
                            detachedOverlay: d,
                            detachedSearchButtonQuery: C,
                            detachedSearchButtonPlaceholder: k,
                            inputWrapper: w,
                            input: S,
                            root: v,
                            form: E,
                            label: h,
                            submitButton: g,
                            clearButton: O,
                            loadingIndicator: _,
                            panel: D
                        }
                    }({
                        autocomplete: v.value,
                        autocompleteScopeApi: b,
                        classNames: p.value.renderer.classNames,
                        environment: p.value.core.environment,
                        isDetached: m.value,
                        placeholder: p.value.core.placeholder,
                        propGetters: d,
                        setIsModalOpen: j,
                        state: y.current,
                        translations: p.value.renderer.translations
                    })
                }));

            function O() {
                Ft(h.value.panel, {
                    style: m.value ? {} : wr({
                        panelPlacement: p.value.renderer.panelPlacement,
                        container: h.value.root,
                        form: h.value.form,
                        environment: p.value.core.environment
                    })
                })
            }

            function _(e) {
                y.current = e;
                var t = {
                    autocomplete: v.value,
                    autocompleteScopeApi: b,
                    classNames: p.value.renderer.classNames,
                    components: p.value.renderer.components,
                    container: p.value.renderer.container,
                    html: g.value,
                    dom: h.value,
                    panelContainer: m.value ? h.value.detachedContainer : p.value.renderer.panelContainer,
                    propGetters: d,
                    state: y.current,
                    renderer: p.value.renderer.renderer
                },
                    n = !$(e) && !l.current && p.value.renderer.renderNoResults || p.value.renderer.render;
                ! function (e) {
                    var t = e.autocomplete,
                        n = e.autocompleteScopeApi,
                        r = e.dom,
                        o = e.propGetters,
                        i = e.state;
                    Ut(r.root, o.getRootProps(Dr({
                        state: i,
                        props: t.getRootProps({})
                    }, n))), Ut(r.input, o.getInputProps(Dr({
                        state: i,
                        props: t.getInputProps({
                            inputElement: r.input
                        }),
                        inputElement: r.input
                    }, n))), Ft(r.label, {
                        hidden: "stalled" === i.status
                    }), Ft(r.loadingIndicator, {
                        hidden: "stalled" !== i.status
                    }), Ft(r.clearButton, {
                        hidden: !i.query
                    }), Ft(r.detachedSearchButtonQuery, {
                        textContent: i.query
                    }), Ft(r.detachedSearchButtonPlaceholder, {
                        hidden: Boolean(i.query)
                    })
                }(t),
                    function (e, t) {
                        var n = t.autocomplete,
                            r = t.autocompleteScopeApi,
                            o = t.classNames,
                            i = t.html,
                            u = t.dom,
                            a = t.panelContainer,
                            c = t.propGetters,
                            l = t.state,
                            s = t.components,
                            f = t.renderer;
                        if (l.isOpen) {
                            a.contains(u.panel) || "loading" === l.status || a.appendChild(u.panel), u.panel.classList.toggle("aa-Panel--stalled", "stalled" === l.status);
                            var p = l.collections.filter((function (e) {
                                var t = e.source,
                                    n = e.items;
                                return t.templates.noResults || n.length > 0
                            })).map((function (e, t) {
                                var u = e.source,
                                    a = e.items;
                                return f.createElement("section", {
                                    key: t,
                                    className: o.source,
                                    "data-autocomplete-source-id": u.sourceId
                                }, u.templates.header && f.createElement("div", {
                                    className: o.sourceHeader
                                }, u.templates.header({
                                    components: s,
                                    createElement: f.createElement,
                                    Fragment: f.Fragment,
                                    items: a,
                                    source: u,
                                    state: l,
                                    html: i
                                })), u.templates.noResults && 0 === a.length ? f.createElement("div", {
                                    className: o.sourceNoResults
                                }, u.templates.noResults({
                                    components: s,
                                    createElement: f.createElement,
                                    Fragment: f.Fragment,
                                    source: u,
                                    state: l,
                                    html: i
                                })) : f.createElement("ul", Er({
                                    className: o.list
                                }, c.getListProps(Dr({
                                    state: l,
                                    props: n.getListProps({
                                        source: u
                                    })
                                }, r))), a.map((function (e) {
                                    var t = n.getItemProps({
                                        item: e,
                                        source: u
                                    });
                                    return f.createElement("li", Er({
                                        key: t.id,
                                        className: o.item
                                    }, c.getItemProps(Dr({
                                        state: l,
                                        props: t
                                    }, r))), u.templates.item({
                                        components: s,
                                        createElement: f.createElement,
                                        Fragment: f.Fragment,
                                        item: e,
                                        state: l,
                                        html: i
                                    }))
                                }))), u.templates.footer && f.createElement("div", {
                                    className: o.sourceFooter
                                }, u.templates.footer({
                                    components: s,
                                    createElement: f.createElement,
                                    Fragment: f.Fragment,
                                    items: a,
                                    source: u,
                                    state: l,
                                    html: i
                                })))
                            })),
                                m = f.createElement(f.Fragment, null, f.createElement("div", {
                                    className: o.panelLayout
                                }, p), f.createElement("div", {
                                    className: "aa-GradientBottom"
                                })),
                                v = p.reduce((function (e, t) {
                                    return e[t.props["data-autocomplete-source-id"]] = t, e
                                }), {});
                            e(Dr(Dr({
                                children: m,
                                state: l,
                                sections: p,
                                elements: v
                            }, f), {}, {
                                components: s,
                                html: i
                            }, r), u.panel)
                        } else a.contains(u.panel) && a.removeChild(u.panel)
                    }(n, t)
            }

            function S() {
                var e = arguments.length > 0 && void 0 !== arguments[0] ? arguments[0] : {};
                o();
                var t, n, r = p.value.renderer,
                    u = r.components,
                    a = $r(r, Vr);
                s.current = qr(a, p.value.core, {
                    components: (t = u, n = function (e) {
                        return !e.value.hasOwnProperty("__autocomplete_componentName")
                    }, Object.entries(t).reduce((function (e, t) {
                        var r = Mr(t, 2),
                            o = r[0],
                            i = r[1];
                        return n({
                            key: o,
                            value: i
                        }) ? Fr(Fr({}, e), {}, Ur({}, o, i)) : e
                    }), {})),
                    initialState: y.current
                }, e), c(), i(), v.value.refresh().then((function () {
                    _(y.current)
                }))
            }

            function j(e) {
                e !== p.value.core.environment.document.body.contains(h.value.detachedOverlay) && (e ? (p.value.core.environment.document.body.appendChild(h.value.detachedOverlay), p.value.core.environment.document.body.classList.add("aa-Detached"), h.value.input.focus()) : (p.value.core.environment.document.body.removeChild(h.value.detachedOverlay), p.value.core.environment.document.body.classList.remove("aa-Detached")))
            }
            return r((function () {
                var e = v.value.getEnvironmentProps({
                    formElement: h.value.form,
                    panelElement: h.value.panel,
                    inputElement: h.value.input
                });
                return Ft(p.value.core.environment, e),
                    function () {
                        Ft(p.value.core.environment, Object.keys(e).reduce((function (e, t) {
                            return Kr(Kr({}, e), {}, zr({}, t, void 0))
                        }), {}))
                    }
            })), r((function () {
                var e = m.value ? p.value.core.environment.document.body : p.value.renderer.panelContainer,
                    t = m.value ? h.value.detachedOverlay : h.value.panel;
                return m.value && y.current.isOpen && j(!0), _(y.current),
                    function () {
                        e.contains(t) && (e.removeChild(t), e.classList.remove("aa-Detached"))
                    }
            })), r((function () {
                var e = p.value.renderer.container;
                return e.appendChild(h.value.root),
                    function () {
                        e.removeChild(h.value.root)
                    }
            })), r((function () {
                var e = Et((function (e) {
                    _(e.state)
                }), 0);
                return f.current = function (t) {
                    var n = t.state,
                        r = t.prevState;
                    (m.value && r.isOpen !== n.isOpen && j(n.isOpen), m.value || !n.isOpen || r.isOpen || O(), n.query !== r.query) && p.value.core.environment.document.querySelectorAll(".aa-Panel--scrollable").forEach((function (e) {
                        0 !== e.scrollTop && (e.scrollTop = 0)
                    }));
                    e({
                        state: n
                    })
                },
                    function () {
                        f.current = void 0
                    }
            })), r((function () {
                var e = Et((function () {
                    var e = m.value;
                    m.value = p.value.core.environment.matchMedia(p.value.renderer.detachedMediaQuery).matches, e !== m.value ? S({}) : requestAnimationFrame(O)
                }), 20);
                return p.value.core.environment.addEventListener("resize", e),
                    function () {
                        p.value.core.environment.removeEventListener("resize", e)
                    }
            })), r((function () {
                if (!m.value) return function () { };

                function e(e) {
                    h.value.detachedContainer.classList.toggle("aa-DetachedContainer--modal", e)
                }

                function t(t) {
                    e(t.matches)
                }
                var n = p.value.core.environment.matchMedia(getComputedStyle(p.value.core.environment.document.documentElement).getPropertyValue("--aa-detached-modal-media-query"));
                e(n.matches);
                var r = Boolean(n.addEventListener);
                return r ? n.addEventListener("change", t) : n.addListener(t),
                    function () {
                        r ? n.removeEventListener("change", t) : n.removeListener(t)
                    }
            })), r((function () {
                return requestAnimationFrame(O),
                    function () { }
            })), Kr(Kr({}, b), {}, {
                update: S,
                destroy: function () {
                    o()
                }
            })
        }
        try {
            window.autocomplete = Gr
        } catch (sn) { }
        return t
    }()
}));