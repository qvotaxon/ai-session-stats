import type { ComponentProps } from "react";
import ReactMarkdown, { type Components } from "react-markdown";
import remarkGfm from "remark-gfm";

// Raw HTML is never rendered: no rehype-raw, and react-markdown drops HTML nodes by default.
const components: Components = {
  h1: (props) => <h1 className="mt-6 mb-3 text-2xl font-semibold tracking-tight first:mt-0" {...omitNode(props)} />,
  h2: (props) => <h2 className="mt-6 mb-3 border-b pb-1 text-xl font-semibold first:mt-0" {...omitNode(props)} />,
  h3: (props) => <h3 className="mt-5 mb-2 text-lg font-semibold" {...omitNode(props)} />,
  h4: (props) => <h4 className="mt-4 mb-2 font-semibold" {...omitNode(props)} />,
  p: (props) => <p className="my-3 leading-7" {...omitNode(props)} />,
  ul: (props) => <ul className="my-3 ml-6 list-disc space-y-1" {...omitNode(props)} />,
  ol: (props) => <ol className="my-3 ml-6 list-decimal space-y-1" {...omitNode(props)} />,
  blockquote: (props) => (
    <blockquote className="my-3 border-l-4 pl-4 text-muted-foreground italic" {...omitNode(props)} />
  ),
  a: (props) => (
    <a
      className="text-primary underline underline-offset-4"
      target="_blank"
      rel="noopener noreferrer"
      {...omitNode(props)}
    />
  ),
  // Report text is LLM output and may be prompt-injected: an <img> would fire a network request (data exfiltration)
  // without a click, so images become plain links that show their URL.
  img: ({ src, alt }) =>
    typeof src === "string" && src ? (
      <a
        className="text-primary underline underline-offset-4 break-all"
        href={src}
        target="_blank"
        rel="noopener noreferrer"
      >
        {alt ? `${alt} (${src})` : src}
      </a>
    ) : (
      <span>{alt}</span>
    ),
  hr: () => <hr className="my-6" />,
  pre: (props) => <pre className="my-3 overflow-x-auto rounded-md bg-muted p-3 text-sm" {...omitNode(props)} />,
  code: (props) => <code className="rounded bg-muted px-1 py-0.5 font-mono text-[0.85em]" {...omitNode(props)} />,
  table: (props) => (
    <div className="my-4 overflow-x-auto">
      <table className="w-full border-collapse text-sm" {...omitNode(props)} />
    </div>
  ),
  th: (props) => <th className="border bg-muted px-3 py-1.5 text-left font-semibold" {...omitNode(props)} />,
  td: (props) => <td className="border px-3 py-1.5 align-top" {...omitNode(props)} />,
};

/** react-markdown passes the hast `node` prop to custom components; it must not reach the DOM. */
function omitNode<T extends { node?: unknown }>(props: T): Omit<T, "node"> {
  const rest: Omit<T, "node"> & { node?: unknown } = { ...props };
  delete rest.node;
  return rest;
}

export const MarkdownView = ({
  children,
  className,
}: { children: string } & Pick<ComponentProps<"div">, "className">) => (
  <div className={className}>
    <ReactMarkdown remarkPlugins={[remarkGfm]} components={components}>
      {children}
    </ReactMarkdown>
  </div>
);
